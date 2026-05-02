namespace SkipWatch.Core.Services.YouTube.Models;

public class YouTubeApiSettings
{
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>YouTube Data API v3 hard daily quota — the value imposed by Google.</summary>
    public int DailyQuotaLimit { get; set; } = 10000;

    /// <summary>
    /// Soft ceiling — quota reservations past this point are refused, deferring further
    /// API calls until UTC rollover. Leaves headroom under the hard limit for ad-hoc work.
    /// Surfaced to the user as YOUTUBE_DAILY_QUOTA_CEILING.
    /// </summary>
    public int CeilingUnits { get; set; } = 9000;

    public int QuotaWarningThreshold { get; set; } = 80;
    public int QuotaCriticalThreshold { get; set; } = 95;

    public int MaxConcurrentRequests { get; set; } = 5;
    public int RequestTimeoutSeconds { get; set; } = 30;

    public bool EnablePersistentQuotaStorage { get; set; } = true;

    /// <summary>
    /// File path for storing quota data. If empty, uses ~/.skipwatch/youtube_quota.json.
    /// </summary>
    public string QuotaStorageFilePath { get; set; } = string.Empty;

    public int QuotaResetHour { get; set; } = 0;
    public bool EnableQuotaLogging { get; set; } = true;
}

public enum YouTubeApiOperation
{
    SearchChannels,
    GetChannelDetails,
    GetChannelVideos,
    SearchVideos,
    GetVideoDetails,
    GetVideoComments,
    GetPlaylistDetails,
    GetPlaylistItems
}

public class YouTubeQuotaStatus
{
    public int DailyLimit { get; set; }
    public int Used { get; set; }
    public int Remaining => DailyLimit - Used;
    public int Reserved { get; set; }
    public int AvailableForUse => Remaining - Reserved;
    public double UsagePercentage => DailyLimit > 0 ? (double)Used / DailyLimit * 100 : 0;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public DateTime NextReset { get; set; }
    public TimeSpan TimeUntilReset => NextReset - DateTime.UtcNow;
    public bool IsExhausted => AvailableForUse <= 0;
    public bool IsNearLimit { get; set; }
    public bool IsCritical { get; set; }
}

public class QuotaReservationResult
{
    public bool Success { get; set; }
    public string ReservationToken { get; set; } = string.Empty;
    public int ReservedQuota { get; set; }
    public DateTime ReservationExpiry { get; set; }
    public string FailureReason { get; set; } = string.Empty;
    public Dictionary<YouTubeApiOperation, int> RequestedOperations { get; set; } = new();
}

public class QuotaThresholdEventArgs : EventArgs
{
    public YouTubeQuotaStatus QuotaStatus { get; set; } = new();
    public string ThresholdType { get; set; } = string.Empty; // "Warning" | "Critical"
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class QuotaExhaustedEventArgs : EventArgs
{
    public YouTubeQuotaStatus QuotaStatus { get; set; } = new();
    public string Message { get; set; } = string.Empty;
    public DateTime ExhaustedAt { get; set; } = DateTime.UtcNow;
    public DateTime NextResetAt { get; set; }
}

public class QuotaStorageData
{
    public DateTime LastReset { get; set; }
    public int QuotaUsed { get; set; }
    public Dictionary<string, QuotaReservation> ActiveReservations { get; set; } = new();
    public DateTime LastSaved { get; set; } = DateTime.UtcNow;
}

public class QuotaReservation
{
    public string Token { get; set; } = string.Empty;
    public int ReservedQuota { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public Dictionary<YouTubeApiOperation, int> Operations { get; set; } = new();
    public bool IsExpired => DateTime.UtcNow > ExpiresAt;
}

public static class YouTubeApiOperationCosts
{
    private static readonly Dictionary<YouTubeApiOperation, int> _operationCosts = new()
    {
        { YouTubeApiOperation.SearchChannels, 100 },
        { YouTubeApiOperation.GetChannelDetails, 1 },
        { YouTubeApiOperation.GetChannelVideos, 1 },
        { YouTubeApiOperation.SearchVideos, 100 },
        { YouTubeApiOperation.GetVideoDetails, 1 },
        { YouTubeApiOperation.GetVideoComments, 1 },
        { YouTubeApiOperation.GetPlaylistDetails, 1 },
        { YouTubeApiOperation.GetPlaylistItems, 1 }
    };

    public static int GetCost(YouTubeApiOperation operation) =>
        _operationCosts.TryGetValue(operation, out var cost) ? cost : 1;

    public static IReadOnlyDictionary<YouTubeApiOperation, int> GetAllCosts() =>
        _operationCosts.AsReadOnly();
}
