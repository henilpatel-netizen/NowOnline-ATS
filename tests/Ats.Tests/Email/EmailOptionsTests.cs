using Ats.Application.Email;
using Xunit;

namespace Ats.Tests.Email;

public class EmailOptionsTests
{
    private static EmailOptions Valid() => new()
    {
        FromAddress = "ats@x.nl",
        Smtp = new SmtpSettings { Host = "smtp.gmail.com" }
    };

    [Fact]
    public void Valid_options_have_no_errors() =>
        Assert.Empty(EmailOptions.Validate(Valid(), isDevelopment: false));

    [Fact]
    public void Redirect_outside_development_is_an_error()
    {
        var o = Valid();
        o.RedirectAllTo = "dev@x.nl";
        Assert.Single(EmailOptions.Validate(o, isDevelopment: false));
    }

    [Fact]
    public void Redirect_in_development_is_allowed()
    {
        var o = Valid();
        o.RedirectAllTo = "dev@x.nl";
        Assert.Empty(EmailOptions.Validate(o, isDevelopment: true));
    }

    [Fact]
    public void Missing_host_and_pickup_directory_is_an_error()
    {
        var o = Valid();
        o.Smtp.Host = "";
        Assert.Single(EmailOptions.Validate(o, isDevelopment: true));
    }

    [Fact]
    public void Pickup_directory_without_host_is_allowed()
    {
        var o = Valid();
        o.Smtp.Host = "";
        o.PickupDirectory = "mail";
        Assert.Empty(EmailOptions.Validate(o, isDevelopment: true));
    }

    [Fact]
    public void Missing_from_address_is_an_error()
    {
        var o = Valid();
        o.FromAddress = " ";
        Assert.Single(EmailOptions.Validate(o, isDevelopment: true));
    }

    [Theory]
    [InlineData("Tls")]
    [InlineData("")]
    [InlineData("Auto")]
    public void Unknown_security_is_an_error(string security)
    {
        var o = Valid();
        o.Smtp.Security = security;
        Assert.Single(EmailOptions.Validate(o, isDevelopment: true));
    }

    [Theory]
    [InlineData("None")]
    [InlineData("StartTls")]
    [InlineData("SslOnConnect")]
    public void Known_security_is_allowed(string security)
    {
        var o = Valid();
        o.Smtp.Security = security;
        Assert.Empty(EmailOptions.Validate(o, isDevelopment: true));
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("a@")]
    public void Invalid_from_address_is_an_error(string from)
    {
        var o = Valid();
        o.FromAddress = from;
        Assert.Single(EmailOptions.Validate(o, isDevelopment: true));
    }

    [Fact]
    public void Invalid_redirect_address_is_an_error()
    {
        var o = Valid();
        o.RedirectAllTo = "not-an-address";
        Assert.Single(EmailOptions.Validate(o, isDevelopment: true));
    }

    [Theory]
    [InlineData(nameof(EmailOptions.PollSeconds), 0)]
    [InlineData(nameof(EmailOptions.BatchSize), 0)]
    [InlineData(nameof(EmailOptions.BatchSize), 501)]
    [InlineData(nameof(EmailOptions.MaxAttempts), 0)]
    [InlineData(nameof(EmailOptions.BaseBackoffSeconds), 0)]
    [InlineData(nameof(EmailOptions.MaxBackoffSeconds), 29)]
    [InlineData(nameof(EmailOptions.ClaimLeaseSeconds), 119)]
    public void Out_of_range_number_is_an_error(string property, int value)
    {
        var o = Valid();
        typeof(EmailOptions).GetProperty(property)!.SetValue(o, value);
        Assert.Single(EmailOptions.Validate(o, isDevelopment: true));
    }

    [Theory]
    [InlineData(nameof(EmailOptions.PollSeconds), 1)]
    [InlineData(nameof(EmailOptions.BatchSize), 1)]
    [InlineData(nameof(EmailOptions.BatchSize), 500)]
    [InlineData(nameof(EmailOptions.MaxAttempts), 1)]
    [InlineData(nameof(EmailOptions.MaxBackoffSeconds), 30)]
    [InlineData(nameof(EmailOptions.ClaimLeaseSeconds), 120)]
    public void Boundary_number_is_allowed(string property, int value)
    {
        var o = Valid();
        typeof(EmailOptions).GetProperty(property)!.SetValue(o, value);
        Assert.Empty(EmailOptions.Validate(o, isDevelopment: true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Out_of_range_port_is_an_error(int port)
    {
        var o = Valid();
        o.Smtp.Port = port;
        Assert.Single(EmailOptions.Validate(o, isDevelopment: true));
    }
}
