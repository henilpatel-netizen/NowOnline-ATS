namespace Ats.Domain.Enums;

// How an application entered the system. Presentation only: never read by the outbox,
// the worker, the vacancy feed, or the ReferralTool client. Rows that predate the column
// are Unknown, which the UI renders as "Not recorded" rather than guessing a source.
public enum ApplicationOrigin
{
    Unknown = 0,
    CareerSite = 1,
    Manual = 2,
    Referral = 3
}
