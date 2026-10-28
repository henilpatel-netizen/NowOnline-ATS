using Ats.Application.Common;
using Xunit;

namespace Ats.Tests.Common;

public class EmailAddressTests
{
    [Theory]
    [InlineData("  Ada@Acme.TEST ", "ada@acme.test")]
    [InlineData("ada@acme.test", "ada@acme.test")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void Normalise_trims_and_lower_cases(string? input, string expected) =>
        Assert.Equal(expected, EmailAddress.Normalise(input));

    [Theory]
    [InlineData("ada@acme.test", true)]
    [InlineData("", false)]
    [InlineData("not an email", false)]
    [InlineData("a@", false)]
    [InlineData("Ada <ada@acme.test>", false)]
    public void IsValid_accepts_a_bare_address_only(string email, bool expected) =>
        Assert.Equal(expected, EmailAddress.IsValid(email));

    [Fact]
    public void IsValid_caps_the_length_at_256()
    {
        var at256 = new string('a', 244) + "@example.com";
        Assert.Equal(256, at256.Length);
        Assert.True(EmailAddress.IsValid(at256));
        Assert.False(EmailAddress.IsValid("a" + at256));
    }
}
