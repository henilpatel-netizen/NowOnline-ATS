using Ats.Application.Abstractions;
using Ats.Application.Accounts;
using Ats.Application.Tenancy;
using Ats.Web.Identity;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Ats.Web.Controllers;

[AllowAnonymous]
public class AccountController : Controller
{
    // Fixed windows per client IP (Program.cs): EmailRateLimit on Forgot password, which mails any address typed
    // in; TokenRateLimit, looser, on the POSTs that use an emailed link.
    public const string EmailRateLimit = "account-email";
    public const string TokenRateLimit = "account-token";

    private readonly ITenantOnboardingService _onboarding;
    private readonly IIdentityService _identity;
    private readonly IAccountRecoveryService _recovery;
    private readonly IInvitationService _invites;
    private readonly IEmailChangeService _emailChange;

    public AccountController(ITenantOnboardingService onboarding, IIdentityService identity, IAccountRecoveryService recovery,
        IInvitationService invites, IEmailChangeService emailChange)
    {
        _onboarding = onboarding;
        _identity = identity;
        _recovery = recovery;
        _invites = invites;
        _emailChange = emailChange;
    }

    [HttpGet] public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost]
    public async Task<IActionResult> Register(RegisterViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var result = await _onboarding.RegisterAsync(
            new RegisterTenantInput(vm.CompanyName, vm.Slug, vm.OwnerName, vm.OwnerEmail, vm.Password));

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Registration failed.");
            return View(vm);
        }

        var signIn = await _identity.ValidateCredentialsAsync(vm.OwnerEmail, vm.Password);
        if (!signIn.Succeeded) return RedirectToAction("Login");

        await AtsSignIn.SignInAsync(HttpContext, signIn.UserId!.Value, signIn.TenantId!.Value, signIn.Role!,
            signIn.DisplayName ?? "", signIn.SecurityStamp!.Value, signIn.MustChangePassword);
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpGet] public IActionResult Login() => View(new LoginViewModel());

    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var result = await _identity.ValidateCredentialsAsync(vm.Email, vm.Password);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Invalid credentials.");
            return View(vm);
        }

        await AtsSignIn.SignInAsync(HttpContext, result.UserId!.Value, result.TenantId!.Value, result.Role!,
            result.DisplayName ?? "", result.SecurityStamp!.Value, result.MustChangePassword);
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpGet] public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [HttpPost, EnableRateLimiting(EmailRateLimit)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel vm, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(vm);
        await _recovery.RequestResetAsync(vm.Email, ct);
        return View(new ForgotPasswordViewModel { Sent = true });
    }

    // Only renders: a mail scanner prefetching the link must not use it up.
    [HttpGet]
    public async Task<IActionResult> ResetPassword(string? token, CancellationToken ct)
    {
        Response.Headers.Append("Referrer-Policy", "no-referrer");   // the token is in this page's URL
        return View(new ResetPasswordViewModel { Token = token ?? "", LinkUsable = await _recovery.IsResetTokenUsableAsync(token, ct) });
    }

    [HttpPost, EnableRateLimiting(TokenRateLimit)]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel vm, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(vm);
        var result = await _recovery.ResetAsync(vm.Token, vm.NewPassword, ct);
        if (result.Error == AccountRecoveryService.InvalidLink) return View(new ResetPasswordViewModel { LinkUsable = false });
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return View(vm);
        }

        TempData["Success"] = "Your password has been reset. Sign in with your new password.";
        return RedirectToAction("Login");
    }

    // Only renders, like ResetPassword: a prefetched link stays usable.
    [HttpGet]
    public async Task<IActionResult> AcceptInvite(string? token, CancellationToken ct)
    {
        Response.Headers.Append("Referrer-Policy", "no-referrer");   // the token is in this page's URL
        return View(new AcceptInviteViewModel { Token = token ?? "", Invite = await _invites.ReadUsableAsync(token, ct) });
    }

    [HttpPost, EnableRateLimiting(TokenRateLimit)]
    public async Task<IActionResult> AcceptInvite(AcceptInviteViewModel vm, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            vm.Invite = await _invites.ReadUsableAsync(vm.Token, ct);
            return View(vm);
        }
        var result = await _invites.AcceptAsync(vm.Token, vm.NewPassword, ct);
        if (result.Error == InvitationService.InvalidInvite) return View(new AcceptInviteViewModel());
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            vm.Invite = await _invites.ReadUsableAsync(vm.Token, ct);
            return View(vm);
        }

        await AtsSignIn.SignInAsync(HttpContext, result.UserId!.Value, result.TenantId!.Value, result.Role!,
            result.DisplayName ?? "", result.SecurityStamp!.Value, result.MustChangePassword);
        return RedirectToAction("Index", "Dashboard");
    }

    // Only renders, like ResetPassword: a prefetched link stays usable.
    [HttpGet]
    public async Task<IActionResult> ConfirmEmail(string? token, CancellationToken ct)
    {
        Response.Headers.Append("Referrer-Policy", "no-referrer");   // the token is in this page's URL
        return View(new ConfirmEmailViewModel { Token = token ?? "", NewEmail = await _emailChange.ReadUsableAsync(token, ct) });
    }

    [HttpPost, EnableRateLimiting(TokenRateLimit)]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailViewModel vm, CancellationToken ct)
    {
        var result = await _emailChange.ConfirmAsync(vm.Token, ct);
        if (result.Error == EmailChangeService.InvalidLink) return View(new ConfirmEmailViewModel());
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return View(new ConfirmEmailViewModel { Token = vm.Token, NewEmail = await _emailChange.ReadUsableAsync(vm.Token, ct) });
        }

        // No sign-out here: the rotated stamp already ends every session of this user, and the browser may be
        // signed in as someone else.
        TempData["Success"] = "Your email address has been changed. Sign in with your new address.";
        return RedirectToAction("Login");
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AtsSignIn.Scheme);
        return RedirectToAction("Login");
    }
}
