namespace Ats.Application.Applications;

public record AddCandidateToJobInput(
    int JobId, string FirstName, string LastName, string Email, string? Phone);
