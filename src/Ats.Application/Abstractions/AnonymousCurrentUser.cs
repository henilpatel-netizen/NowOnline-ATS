namespace Ats.Application.Abstractions;

// Null object for hosts that have no signed-in user, such as the background worker. Services such
// as AuditLogger and ApplicationService take ICurrentUser, so every host must supply one; this makes
// "there is no user" explicit rather than leaving the container unable to build those services
// (QUAL-6).
public sealed class AnonymousCurrentUser : ICurrentUser
{
    public int? UserId => null;
    public string? Name => null;
    public string? Role => null;
    public bool IsAuthenticated => false;
}
