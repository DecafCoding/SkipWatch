using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkipWatch.Core.Services.Interfaces;
using SkipWatch.Core.Services.YouTube.Models;

namespace SkipWatch.Core.Services.YouTube;

/// <summary>
/// Thread-safe YouTube Data API quota tracker. Persists daily usage + active reservations
/// to a JSON file under ~/.skipwatch/ so quota survives process restarts within a UTC day.
/// </summary>
public class YouTubeQuotaManager : IYouTubeQuotaManager, IDisposable
{
    private readonly YouTubeApiSettings _settings;
    private readonly ILogger<YouTubeQuotaManager> _logger;
    private readonly SemaphoreSlim _quotaSemaphore;
    private readonly ConcurrentDictionary<string, QuotaReservation> _activeReservations;
    private readonly object _quotaLock = new();

    private int _quotaUsed;
    private DateTime _lastReset;
    private readonly string _quotaStorageFilePath;

    private bool _warningThresholdTriggered;
    private bool _criticalThresholdTriggered;

    public event EventHandler<QuotaThresholdEventArgs>? QuotaThresholdReached;
    public event EventHandler<QuotaExhaustedEventArgs>? QuotaExhausted;

    public YouTubeQuotaManager(IOptions<YouTubeApiSettings> settings, ILogger<YouTubeQuotaManager> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        _quotaSemaphore = new SemaphoreSlim(_settings.MaxConcurrentRequests, _settings.MaxConcurrentRequests);
        _activeReservations = new ConcurrentDictionary<string, QuotaReservation>();

        _quotaStorageFilePath = ResolveQuotaStorageFilePath();
        LoadQuotaData();
        ResetQuotaIfNeeded();

        _logger.LogInformation(
            "YouTube quota manager initialized. Hard limit: {Limit}, ceiling: {Ceiling}, used: {Used}",
            _settings.DailyQuotaLimit, _settings.CeilingUnits, _quotaUsed);
    }

    public async Task<bool> IsApiAvailableAsync()
    {
        try
        {
            ResetQuotaIfNeeded();
            var status = await GetQuotaStatusAsync();
            return !status.IsExhausted && _quotaUsed < _settings.CeilingUnits;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking API availability");
            return false;
        }
    }

    public async Task<YouTubeQuotaStatus> GetQuotaStatusAsync()
    {
        ResetQuotaIfNeeded();
        CleanupExpiredReservations();

        int reservedQuota = _activeReservations.Values.Sum(r => r.ReservedQuota);
        var nextReset = GetNextResetTime();

        var status = new YouTubeQuotaStatus
        {
            DailyLimit = _settings.DailyQuotaLimit,
            Used = _quotaUsed,
            Reserved = reservedQuota,
            LastUpdated = DateTime.UtcNow,
            NextReset = nextReset,
            IsNearLimit = _quotaUsed >= (_settings.DailyQuotaLimit * _settings.QuotaWarningThreshold / 100.0),
            IsCritical = _quotaUsed >= (_settings.DailyQuotaLimit * _settings.QuotaCriticalThreshold / 100.0)
        };

        await CheckAndTriggerThresholdEventsAsync(status);
        return status;
    }

    public async Task<bool> TryConsumeQuotaAsync(YouTubeApiOperation operation, int requestCount = 1)
    {
        if (requestCount <= 0)
            throw new ArgumentException("Request count must be positive", nameof(requestCount));

        int cost = GetOperationCost(operation, requestCount);
        var consumed = await TryConsumeUnitsAsync(cost, operation.ToString(), requestCount);

        if (consumed)
        {
            var currentStatus = await GetQuotaStatusAsync();
            if (currentStatus.IsExhausted)
                await TriggerQuotaExhaustedEventAsync(currentStatus);
        }

        return consumed;
    }

    public Task<bool> TryReserveAsync(int units)
    {
        if (units <= 0)
            throw new ArgumentException("Unit count must be positive", nameof(units));

        return TryConsumeUnitsAsync(units, "Reserve", 1);
    }

    private async Task<bool> TryConsumeUnitsAsync(int cost, string operationLabel, int requestCount)
    {
        ResetQuotaIfNeeded();
        CleanupExpiredReservations();

        bool success = false;
        await _quotaSemaphore.WaitAsync();
        try
        {
            lock (_quotaLock)
            {
                int reserved = _activeReservations.Values.Sum(r => r.ReservedQuota);
                int availableUnderCeiling = _settings.CeilingUnits - _quotaUsed - reserved;

                if (availableUnderCeiling >= cost)
                {
                    _quotaUsed += cost;
                    success = true;

                    if (_settings.EnableQuotaLogging)
                    {
                        _logger.LogInformation(
                            "Quota consumed: {Operation} x{RequestCount} = {Cost} units. Used: {Used}/{Ceiling} (hard limit {Limit})",
                            operationLabel, requestCount, cost, _quotaUsed, _settings.CeilingUnits, _settings.DailyQuotaLimit);
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "Quota refused for {Operation} x{RequestCount} (cost {Cost}). Under-ceiling available: {Available}",
                        operationLabel, requestCount, cost, availableUnderCeiling);
                }
            }
        }
        finally
        {
            _quotaSemaphore.Release();
        }

        if (success && _settings.EnablePersistentQuotaStorage)
            await SaveQuotaDataAsync();

        return success;
    }

    public async Task<QuotaReservationResult> ReserveQuotaAsync(Dictionary<YouTubeApiOperation, int> operations)
    {
        if (operations == null || operations.Count == 0)
            return new QuotaReservationResult { Success = false, FailureReason = "No operations specified" };

        ResetQuotaIfNeeded();
        CleanupExpiredReservations();

        int totalCost = operations.Sum(op => GetOperationCost(op.Key, op.Value));
        var reservationToken = Guid.NewGuid().ToString();
        var expiryTime = DateTime.UtcNow.AddHours(1);

        lock (_quotaLock)
        {
            int reserved = _activeReservations.Values.Sum(r => r.ReservedQuota);
            int availableUnderCeiling = _settings.CeilingUnits - _quotaUsed - reserved;

            if (availableUnderCeiling >= totalCost)
            {
                var reservation = new QuotaReservation
                {
                    Token = reservationToken,
                    ReservedQuota = totalCost,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = expiryTime,
                    Operations = operations
                };

                _activeReservations[reservationToken] = reservation;

                _logger.LogInformation("Quota reserved: {Token} for {Cost} units (expires {Expiry})",
                    reservationToken, totalCost, expiryTime);

                return new QuotaReservationResult
                {
                    Success = true,
                    ReservationToken = reservationToken,
                    ReservedQuota = totalCost,
                    ReservationExpiry = expiryTime,
                    RequestedOperations = operations
                };
            }

            return new QuotaReservationResult
            {
                Success = false,
                FailureReason = $"Insufficient quota under ceiling. Required: {totalCost}, available: {availableUnderCeiling}",
                RequestedOperations = operations
            };
        }
    }

    public async Task<bool> ConfirmReservationAsync(string reservationToken)
    {
        if (string.IsNullOrWhiteSpace(reservationToken))
            return false;

        if (!_activeReservations.TryRemove(reservationToken, out var reservation))
        {
            _logger.LogWarning("Reservation not found: {Token}", reservationToken);
            return false;
        }

        if (reservation.IsExpired)
        {
            _logger.LogWarning("Reservation expired: {Token}", reservationToken);
            return false;
        }

        lock (_quotaLock)
        {
            _quotaUsed += reservation.ReservedQuota;
            _logger.LogInformation("Reservation confirmed: {Token} for {Cost} units. Used: {Used}/{Ceiling}",
                reservationToken, reservation.ReservedQuota, _quotaUsed, _settings.CeilingUnits);
        }

        if (_settings.EnablePersistentQuotaStorage)
            await SaveQuotaDataAsync();

        return true;
    }

    public Task<bool> ReleaseReservationAsync(string reservationToken)
    {
        if (string.IsNullOrWhiteSpace(reservationToken))
            return Task.FromResult(false);

        if (_activeReservations.TryRemove(reservationToken, out var reservation))
        {
            _logger.LogInformation("Reservation released: {Token} for {Cost} units",
                reservationToken, reservation.ReservedQuota);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public int GetOperationCost(YouTubeApiOperation operation, int requestCount = 1)
    {
        if (requestCount <= 0)
            throw new ArgumentException("Request count must be positive", nameof(requestCount));

        return YouTubeApiOperationCosts.GetCost(operation) * requestCount;
    }

    public async Task<bool> ForceQuotaResetAsync()
    {
        try
        {
            lock (_quotaLock)
            {
                _quotaUsed = 0;
                _lastReset = DateTime.UtcNow;
                _activeReservations.Clear();
                _warningThresholdTriggered = false;
                _criticalThresholdTriggered = false;
            }

            if (_settings.EnablePersistentQuotaStorage)
                await SaveQuotaDataAsync();

            _logger.LogWarning("Quota manually reset");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during manual quota reset");
            return false;
        }
    }

    public async Task<bool> CanPerformOperationAsync(YouTubeApiOperation operation, int requestCount = 1)
    {
        ResetQuotaIfNeeded();
        CleanupExpiredReservations();

        int cost = GetOperationCost(operation, requestCount);
        var status = await GetQuotaStatusAsync();
        int reserved = _activeReservations.Values.Sum(r => r.ReservedQuota);
        int availableUnderCeiling = _settings.CeilingUnits - status.Used - reserved;

        return availableUnderCeiling >= cost;
    }

    public TimeSpan GetTimeUntilReset()
    {
        var nextReset = GetNextResetTime();
        var timeUntilReset = nextReset - DateTime.UtcNow;
        return timeUntilReset > TimeSpan.Zero ? timeUntilReset : TimeSpan.Zero;
    }

    #region Private helpers

    private void ResetQuotaIfNeeded()
    {
        var nextReset = GetNextResetTime();
        var shouldReset = DateTime.UtcNow >= nextReset && _lastReset < nextReset.AddDays(-1);

        if (!shouldReset) return;

        lock (_quotaLock)
        {
            if (_lastReset.Date < DateTime.UtcNow.Date)
            {
                _quotaUsed = 0;
                _lastReset = DateTime.UtcNow;
                _activeReservations.Clear();
                _warningThresholdTriggered = false;
                _criticalThresholdTriggered = false;
                _logger.LogInformation("Daily quota reset completed");
            }
        }

        if (_settings.EnablePersistentQuotaStorage)
            _ = Task.Run(SaveQuotaDataAsync);
    }

    private DateTime GetNextResetTime()
    {
        var today = DateTime.UtcNow.Date;
        var resetTime = today.AddHours(_settings.QuotaResetHour);
        if (DateTime.UtcNow >= resetTime)
            resetTime = resetTime.AddDays(1);
        return resetTime;
    }

    private void CleanupExpiredReservations()
    {
        var expiredTokens = _activeReservations
            .Where(r => r.Value.IsExpired)
            .Select(r => r.Key)
            .ToList();

        foreach (var token in expiredTokens)
        {
            if (_activeReservations.TryRemove(token, out var reservation))
            {
                _logger.LogInformation("Expired reservation removed: {Token} for {Cost} units",
                    token, reservation.ReservedQuota);
            }
        }
    }

    private Task CheckAndTriggerThresholdEventsAsync(YouTubeQuotaStatus status)
    {
        if (!_warningThresholdTriggered && status.IsNearLimit && !status.IsCritical)
        {
            _warningThresholdTriggered = true;
            QuotaThresholdReached?.Invoke(this, new QuotaThresholdEventArgs
            {
                QuotaStatus = status,
                ThresholdType = "Warning",
                Message = $"Quota usage has reached {status.UsagePercentage:F1}% ({status.Used}/{status.DailyLimit})"
            });
        }

        if (!_criticalThresholdTriggered && status.IsCritical)
        {
            _criticalThresholdTriggered = true;
            QuotaThresholdReached?.Invoke(this, new QuotaThresholdEventArgs
            {
                QuotaStatus = status,
                ThresholdType = "Critical",
                Message = $"Quota usage has reached critical level: {status.UsagePercentage:F1}% ({status.Used}/{status.DailyLimit})"
            });
        }

        return Task.CompletedTask;
    }

    private Task TriggerQuotaExhaustedEventAsync(YouTubeQuotaStatus status)
    {
        QuotaExhausted?.Invoke(this, new QuotaExhaustedEventArgs
        {
            QuotaStatus = status,
            Message = $"YouTube API quota exhausted. Used: {status.Used}/{status.DailyLimit}",
            ExhaustedAt = DateTime.UtcNow,
            NextResetAt = status.NextReset
        });

        _logger.LogError("YouTube API quota exhausted. Next reset: {NextReset}", status.NextReset);
        return Task.CompletedTask;
    }

    private string ResolveQuotaStorageFilePath()
    {
        if (!string.IsNullOrWhiteSpace(_settings.QuotaStorageFilePath))
            return _settings.QuotaStorageFilePath;

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".skipwatch");
        Directory.CreateDirectory(dataDir);
        return Path.Combine(dataDir, "youtube_quota.json");
    }

    private void LoadQuotaData()
    {
        if (!_settings.EnablePersistentQuotaStorage || !File.Exists(_quotaStorageFilePath))
        {
            _lastReset = DateTime.UtcNow.Date;
            return;
        }

        try
        {
            var json = File.ReadAllText(_quotaStorageFilePath);
            var data = JsonSerializer.Deserialize<QuotaStorageData>(json);

            if (data != null)
            {
                _quotaUsed = data.QuotaUsed;
                _lastReset = data.LastReset;

                foreach (var reservation in data.ActiveReservations.Values.Where(r => !r.IsExpired))
                    _activeReservations[reservation.Token] = reservation;

                _logger.LogInformation("Quota data loaded from storage. Used: {Used}, last reset: {LastReset}",
                    _quotaUsed, _lastReset);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading quota data from storage. Starting fresh.");
            _lastReset = DateTime.UtcNow.Date;
        }
    }

    private async Task SaveQuotaDataAsync()
    {
        if (!_settings.EnablePersistentQuotaStorage)
            return;

        try
        {
            var data = new QuotaStorageData
            {
                LastReset = _lastReset,
                QuotaUsed = _quotaUsed,
                ActiveReservations = _activeReservations.ToDictionary(r => r.Key, r => r.Value),
                LastSaved = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_quotaStorageFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving quota data to storage");
        }
    }

    #endregion

    public void Dispose()
    {
        if (_settings.EnablePersistentQuotaStorage)
            SaveQuotaDataAsync().GetAwaiter().GetResult();

        _quotaSemaphore.Dispose();
        _logger.LogInformation("YouTube quota manager disposed");
    }
}
