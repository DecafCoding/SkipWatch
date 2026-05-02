using SkipWatch.Core.Services.YouTube.Models;

namespace SkipWatch.Core.Services.Interfaces;

public interface IYouTubeQuotaManager
{
    Task<bool> IsApiAvailableAsync();
    Task<YouTubeQuotaStatus> GetQuotaStatusAsync();
    Task<bool> TryConsumeQuotaAsync(YouTubeApiOperation operation, int requestCount = 1);

    /// <summary>
    /// Simple gate keyed by raw unit count. Consumes <paramref name="units"/> against the
    /// soft <see cref="YouTubeApiSettings.CeilingUnits"/> ceiling and returns true on success.
    /// Returns false (without consuming) when used + units would exceed the ceiling.
    /// Used by background workers that compute their own cost (e.g. CollectionRoundService).
    /// </summary>
    Task<bool> TryReserveAsync(int units);

    Task<QuotaReservationResult> ReserveQuotaAsync(Dictionary<YouTubeApiOperation, int> operations);
    Task<bool> ConfirmReservationAsync(string reservationToken);
    Task<bool> ReleaseReservationAsync(string reservationToken);

    int GetOperationCost(YouTubeApiOperation operation, int requestCount = 1);
    Task<bool> ForceQuotaResetAsync();
    Task<bool> CanPerformOperationAsync(YouTubeApiOperation operation, int requestCount = 1);
    TimeSpan GetTimeUntilReset();

    event EventHandler<QuotaThresholdEventArgs> QuotaThresholdReached;
    event EventHandler<QuotaExhaustedEventArgs> QuotaExhausted;
}
