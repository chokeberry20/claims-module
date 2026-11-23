namespace ClaimsModule.Application.Abstractions.Jobs;

/// <summary>
/// Enqueues background jobs (Hangfire in Infrastructure, P4 task 30). Called only from after-commit
/// event handlers, so a job never sees uncommitted data. Enqueueing the same idempotency key twice
/// is safe: the job is idempotent (task 26).
/// </summary>
public interface IJobScheduler
{
    /// <summary>PostGLReserveChangeJob for an approved reserve transaction (S12.1, BR-R-06).</summary>
    void EnqueueGlPosting(Guid transactionId, string idempotencyKey);
}

/// <summary>GL posting settings (configuration section <c>GlPosting</c>).</summary>
public sealed class GlPostingOptions
{
    public const string SectionName = "GlPosting";

    /// <summary>
    /// Share of simulated GL calls that fail (0..1), to demo Failed postings and the retry (PLAN §6.5).
    /// 0 in normal runs.
    /// </summary>
    public double SimulatedFailureRate { get; set; }

    /// <summary>An approved posting still Pending after this long is re-enqueued by the sweep (task 27a).</summary>
    public TimeSpan StalePendingAfter { get; set; } = TimeSpan.FromMinutes(10);
}
