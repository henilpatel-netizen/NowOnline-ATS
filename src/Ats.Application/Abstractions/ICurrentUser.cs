namespace Ats.Application.Abstractions;

public interface ICurrentUser
{
    int? UserId { get; }
    string? Role { get; }
    bool IsAuthenticated { get; }
}
