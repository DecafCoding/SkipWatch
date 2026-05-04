namespace SkipWatch.Core.Services.Transcripts;

/// <summary>
/// Phase 2 transcript worker tuning. Bound from the <c>TranscriptWorker:</c>
/// configuration section. Per PRD §6 Phase 2: concurrency is fixed at 1 in MVP — local
/// LLM and serial Apify spend make parallelism a v2 concern.
/// </summary>
public sealed class TranscriptWorkerSettings
{
    /// <summary>Worker concurrency. MVP: 1.</summary>
    public int Concurrency { get; set; } = 1;

    /// <summary>Idle sleep interval when no row is eligible.</summary>
    public int IdlePollSeconds { get; set; } = 10;
}
