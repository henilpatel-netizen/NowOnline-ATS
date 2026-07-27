namespace Ats.Application.Career;

public record ApplyInput(
    string ExternalRef, string FirstName, string LastName, string Email,
    string? Phone, string? SourceCode, string? ResumeFileKey);
