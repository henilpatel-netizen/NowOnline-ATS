namespace Ats.Domain.Enums;

public enum OutboxStatus
{
    Pending = 0,
    Delivered = 1,
    Failed = 2,
    // Claimed by a worker and in flight. A visibility-timeout lease (NextAttemptAt) lets a crashed
    // worker's messages be reclaimed once the lease expires. Stored as int; no schema change.
    Processing = 3
}
