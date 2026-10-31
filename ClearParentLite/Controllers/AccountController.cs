using ClearParentLite.Data;
using ClearParentLite.Models;
using ClearParentLite.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;
namespace ClearParentLite.Controllers;
[Route("account"), EnableRateLimiting("account")]
public class AccountController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, RoleManager<IdentityRole> roles, IEmailService email, IConfiguration config, AppDbContext db, AccessService access) : Controller
{
    string Origin => config["App:PublicOrigin"]!.TrimEnd('/');
    static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
    static string Decode(string token) => Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
    async Task ParentRole(ApplicationUser user) { if (!await roles.RoleExistsAsync("Parent")) await roles.CreateAsync(new IdentityRole("Parent")); await users.AddToRoleAsync(user, "Parent"); }
    void Errors(IdentityResult r) { foreach (var e in r.Errors) ModelState.AddModelError("", e.Description); }
    [HttpGet("register")] public IActionResult Register() => View(new RegisterInput());
    [HttpPost("register")] public async Task<IActionResult> Register(RegisterInput input) {
        if (!ModelState.IsValid) return View(input);
        var user = new ApplicationUser { UserName = input.Email, Email = input.Email, DisplayName = input.DisplayName.Trim() };
        var result = await users.CreateAsync(user, input.Password);
        if (!result.Succeeded) { Errors(result); return View(input); }
        await ParentRole(user); await SendConfirmation(user);
        return View("Message", "Check your email to confirm your account before signing in.");
    }
    async Task SendConfirmation(ApplicationUser user) {
        var code = Encode(await users.GenerateEmailConfirmationTokenAsync(user));
        await email.Send(user.Email!, "Confirm your ClearParent Lite account", $"Confirm your account: {Origin}/account/confirm?userId={Uri.EscapeDataString(user.Id)}&code={code}");
    }
    [HttpGet("confirm")] public async Task<IActionResult> Confirm(string userId, string code) {
        var user = await users.FindByIdAsync(userId);
        if (user is null || user.DisabledUtc != null) return BadRequest();
        try { var result = await users.ConfirmEmailAsync(user, Decode(code)); return View("Message", result.Succeeded ? "Email confirmed. You can now sign in." : "This confirmation link is invalid or expired."); } catch (FormatException) { return BadRequest(); }
    }
    [HttpGet("login")] public async Task<IActionResult> Login(string? returnUrl) { ViewBag.Google = (await signIn.GetExternalAuthenticationSchemesAsync()).Any(s => s.Name == "Google"); return View(new LoginInput { ReturnUrl = returnUrl }); }
    [HttpPost("login")] public async Task<IActionResult> Login(LoginInput input) {
        ViewBag.Google = (await signIn.GetExternalAuthenticationSchemesAsync()).Any(s => s.Name == "Google");
        if (!ModelState.IsValid) return View(input);
        var user = await users.FindByEmailAsync(input.Email);
        if (user != null && user.DisabledUtc == null) {
            var result = await signIn.PasswordSignInAsync(user, input.Password, false, true);
            access.Audit(user.Id, result.Succeeded ? "LoginSucceeded" : "LoginFailed", "Account", user.Id); await db.SaveChangesAsync();
            if (result.Succeeded) return LocalRedirect(Url.IsLocalUrl(input.ReturnUrl) ? input.ReturnUrl! : "/timeline");
        }
        ModelState.AddModelError("", "Unable to sign in. Check your credentials and confirm your email, or try again later."); return View(input);
    }
    [Authorize, HttpPost("logout")] public async Task<IActionResult> Logout() { await signIn.SignOutAsync(); Response.Cookies.Delete("cp.share"); return Redirect("/"); }
    [HttpGet("forgot-password")] public IActionResult Forgot() => View(new EmailInput());
    [HttpPost("forgot-password")] public async Task<IActionResult> Forgot(EmailInput input) {
        if (!ModelState.IsValid) return View(input);
        var user = await users.FindByEmailAsync(input.Email);
        if (user is { EmailConfirmed: true, DisabledUtc: null }) { var code = Encode(await users.GeneratePasswordResetTokenAsync(user)); await email.Send(user.Email!, "Reset your ClearParent Lite password", $"Reset your password: {Origin}/account/reset-password?email={Uri.EscapeDataString(user.Email!)}&code={code}"); }
        return View("Message", "If an eligible account exists, a password reset link has been sent.");
    }
    [HttpGet("resend-confirmation")] public IActionResult Resend() => View("Forgot", new EmailInput());
    [HttpPost("resend-confirmation")] public async Task<IActionResult> Resend(EmailInput input) {
        if (!ModelState.IsValid) return View("Forgot", input);
        var user = await users.FindByEmailAsync(input.Email); if (user is { EmailConfirmed: false, DisabledUtc: null }) await SendConfirmation(user);
        return View("Message", "If an eligible account exists, a confirmation email has been sent.");
    }
    [HttpGet("reset-password")] public IActionResult Reset(string email, string code) => View(new ResetInput { Email = email, Code = code });
    [HttpPost("reset-password")] public async Task<IActionResult> Reset(ResetInput input) {
        if (!ModelState.IsValid) return View(input);
        var user = await users.FindByEmailAsync(input.Email);
        if (user is null || !user.EmailConfirmed || user.DisabledUtc != null) return View("Message", "If eligible, your password has been reset.");
        try { var result = await users.ResetPasswordAsync(user, Decode(input.Code), input.Password); if (result.Succeeded) return View("Message", "Password reset. You can now sign in."); Errors(result); } catch (FormatException) { ModelState.AddModelError("", "Invalid reset link."); }
        return View(input);
    }
    [HttpPost("google")] public async Task<IActionResult> Google() {
        if (!(await signIn.GetExternalAuthenticationSchemesAsync()).Any(s => s.Name == "Google")) return View("Message", "Google sign-in is unavailable. Use email/password; configure Google credentials to enable it.");
        return Challenge(signIn.ConfigureExternalAuthenticationProperties("Google", "/account/google-callback"), "Google");
    }
    [HttpGet("google-callback")] public async Task<IActionResult> GoogleCallback() {
        var info = await signIn.GetExternalLoginInfoAsync(); if (info == null) return Redirect("/account/login");
        var existing = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (existing != null && existing.DisabledUtc == null && existing.EmailConfirmed) { await signIn.SignInAsync(existing, false); access.Audit(existing.Id,"GoogleLogin","Account",existing.Id); await db.SaveChangesAsync(); return Redirect("/timeline"); }
        var address = info.Principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
        if (string.IsNullOrWhiteSpace(address) || await users.FindByEmailAsync(address) != null) return View("Message", "An account with this email may already exist. Sign in with your password. Accounts are never linked automatically by email.");
        // Require our own confirmation, even when an external provider supplies an email.
        var user = new ApplicationUser { UserName = address, Email = address, DisplayName = info.Principal.Identity?.Name ?? "Parent" };
        var result = await users.CreateAsync(user); if (!result.Succeeded) return View("Message", "Unable to create this account.");
        var login = await users.AddLoginAsync(user, info); if (!login.Succeeded) { await users.DeleteAsync(user); return BadRequest(); }
        await ParentRole(user); await SendConfirmation(user); return View("Message", "Confirm the email we sent, then sign in with Google again.");
    }
    [HttpGet("denied")] public IActionResult Denied() { Response.StatusCode = 403; return View("Message", "Access denied. A confirmed parent account is required for this action."); }
}
