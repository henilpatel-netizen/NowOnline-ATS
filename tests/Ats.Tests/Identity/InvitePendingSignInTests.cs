using Ats.Domain.Entities;
using Ats.Infrastructure.Identity;
using Xunit;

namespace Ats.Tests.Identity;

// An invited user has no password yet; nothing typed at sign-in may match the empty hash.
public class InvitePendingSignInTests
{
    [Theory]
    [InlineData("")]
    [InlineData("anything")]
    public void An_empty_hash_never_verifies(string password) =>
        Assert.False(new IdentityService(null!).VerifyPassword("", password));

    [Fact]
    public void A_user_without_a_password_is_invite_pending()
    {
        Assert.True(new AppUser { PasswordHash = "" }.IsInvitePending);
        Assert.False(new AppUser { PasswordHash = "hash:x" }.IsInvitePending);
    }
}
