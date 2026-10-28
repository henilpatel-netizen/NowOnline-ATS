namespace Ats.Application.Notifications;

// Stages one email per recipient on the current unit of work; the caller's next save commits them, so a failed
// save sends nothing. Emails carry the candidate's name only, never their email, phone or CV.
public interface IHiringNotifier
{
    Task StageNewApplicationAsync(int jobId, string jobTitle, string candidateName, CancellationToken ct = default);
    Task StageStageChangedAsync(int jobId, string jobTitle, string candidateName, string stageName, int? movedByUserId, CancellationToken ct = default);
}
