using Ats.Application.Abstractions;
using Ats.Web.Identity;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace Ats.Tests.Identity;

public class AccountTokensTests
{
    private sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly FakeTime _time = new();
    private readonly DataProtectionAccountTokens _tokens;
    private readonly AccountToken _token = new(UserId: 7, TenantId: 3, SecurityStamp: Guid.NewGuid());

    public AccountTokensTests() => _tokens = new DataProtectionAccountTokens(new EphemeralDataProtectionProvider(), _time);

    [Theory]
    [InlineData(AccountTokenPurpose.PasswordReset)]
    [InlineData(AccountTokenPurpose.Invite)]
    [InlineData(AccountTokenPurpose.EmailChange)]
    public void Round_trips(AccountTokenPurpose purpose) =>
        Assert.Equal(_token, _tokens.Read(purpose, _tokens.Create(purpose, _token)));

    [Fact]
    public void New_email_survives_the_round_trip()
    {
        var token = _token with { NewEmail = "new@example.com" };

        Assert.Equal(token, _tokens.Read(AccountTokenPurpose.EmailChange, _tokens.Create(AccountTokenPurpose.EmailChange, token)));
    }

    [Fact]
    public void Token_is_url_safe()
    {
        var token = _tokens.Create(AccountTokenPurpose.EmailChange, _token with { NewEmail = "new@example.com" });

        Assert.Matches("^[A-Za-z0-9_-]+$", token);
    }

    [Theory]
    [InlineData(AccountTokenPurpose.PasswordReset, AccountTokenPurpose.Invite)]
    [InlineData(AccountTokenPurpose.Invite, AccountTokenPurpose.EmailChange)]
    [InlineData(AccountTokenPurpose.EmailChange, AccountTokenPurpose.PasswordReset)]
    public void Wrong_purpose_reads_null(AccountTokenPurpose created, AccountTokenPurpose read) =>
        Assert.Null(_tokens.Read(read, _tokens.Create(created, _token)));

    [Fact]
    public void One_changed_character_reads_null()
    {
        var token = _tokens.Create(AccountTokenPurpose.PasswordReset, _token);
        var i = token.Length / 2;
        var tampered = token[..i] + (token[i] == 'A' ? 'B' : 'A') + token[(i + 1)..];

        Assert.Null(_tokens.Read(AccountTokenPurpose.PasswordReset, tampered));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("not base64 !!")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Missing_or_garbage_reads_null(string? token) =>
        Assert.Null(_tokens.Read(AccountTokenPurpose.PasswordReset, token));

    [Theory]
    [InlineData(AccountTokenPurpose.PasswordReset, 59, true)]
    [InlineData(AccountTokenPurpose.PasswordReset, 61, false)]
    [InlineData(AccountTokenPurpose.Invite, 6 * 24 * 60, true)]
    [InlineData(AccountTokenPurpose.Invite, 8 * 24 * 60, false)]
    [InlineData(AccountTokenPurpose.EmailChange, 23 * 60, true)]
    [InlineData(AccountTokenPurpose.EmailChange, 25 * 60, false)]
    public void Expires_after_the_purpose_lifetime(AccountTokenPurpose purpose, int minutesLater, bool valid)
    {
        var token = _tokens.Create(purpose, _token);
        _time.Now = _time.Now.AddMinutes(minutesLater);

        var read = _tokens.Read(purpose, token);

        if (valid) Assert.Equal(_token, read);
        else Assert.Null(read);
    }
}
