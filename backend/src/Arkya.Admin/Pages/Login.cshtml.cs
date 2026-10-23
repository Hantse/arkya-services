using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Arkya.Admin.Authentication;

namespace Arkya.Admin.Pages;

public sealed class LoginModel(IOptions<AdminOptions> options) : PageModel
{
    [BindProperty, Required, StringLength(512)] public string Password { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public bool DiscordEnabled => options.Value.DiscordEnabled;

    public IActionResult OnGet(string? error)
    {
        if (error == "discord") ModelState.AddModelError("", "Discord sign-in was denied or interrupted. Use the authorised administrator account and try again.");
        return User.Identity?.IsAuthenticated == true
            ? LocalRedirect(AdminDiscordAuthentication.LocalReturnUrl(ReturnUrl)) : Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (DiscordEnabled) return NotFound();
        if (!ModelState.IsValid) return Page();
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(Password));
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.Password));
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            ModelState.AddModelError("", "Incorrect password.");
            return Page();
        }
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Name, "Administrator"), new Claim(ClaimTypes.Role, "Administrator")
        ], CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/");
    }

    public IActionResult OnPostDiscord()
    {
        if (!DiscordEnabled) return NotFound();
        return Challenge(new AuthenticationProperties
        {
            RedirectUri = AdminDiscordAuthentication.LocalReturnUrl(ReturnUrl)
        }, AdminDiscordAuthentication.Scheme);
    }
}
