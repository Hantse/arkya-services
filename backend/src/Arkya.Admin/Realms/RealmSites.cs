using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Arkya.Admin.Realms;

public sealed class RealmSiteOptions
{
    public bool Enabled { get; init; }
    public string BaseDomain { get; init; } = "arkya.online";

    public bool IsValid() => !Enabled || BaseDomain.Length <= 180 && BaseDomain.Contains('.') &&
        BaseDomain == BaseDomain.ToLowerInvariant() && !BaseDomain.EndsWith('.') &&
        Uri.CheckHostName(BaseDomain) == UriHostNameType.Dns;
}

public sealed class RealmSiteLinks(IOptions<RealmSiteOptions> options)
{
    public bool Enabled => options.Value.Enabled;
    public string AdminDomain => "admin." + options.Value.BaseDomain;
    public string AdminOrigin => "https://" + AdminDomain;
    public string? CookieDomain => Enabled ? AdminDomain : null;

    public static bool IsRealmId(string? id) => id is { Length: >= 3 and <= 31 } &&
        Regex.IsMatch(id, "\\A[a-z][a-z0-9-]{1,29}[a-z0-9]\\z", RegexOptions.CultureInvariant) &&
        id is not ("api" or "admin" or "www" or "backoffice" or "beta-game");

    public string? RealmId(HttpContext context)
    {
        var host = context.Request.Host.Host.ToLowerInvariant();
        var suffix = "." + AdminDomain;
        if (!Enabled || !host.EndsWith(suffix, StringComparison.Ordinal)) return null;
        var id = host[..^suffix.Length];
        return IsRealmId(id) ? id : null;
    }

    public string AdminUrl(string id) => Enabled && IsRealmId(id)
        ? "https://" + id + "." + AdminDomain + "/" : "/Realms/" + Uri.EscapeDataString(id);
    public string PublicUrl(string id) => "https://" + id + "." + options.Value.BaseDomain + "/";

    public string LoginUrl(HttpContext context)
    {
        var id = RealmId(context)!;
        var returnUrl = "/Realms/" + id + context.Request.QueryString;
        return QueryHelpers.AddQueryString(AdminOrigin + "/Login", "ReturnUrl", returnUrl);
    }
}

public static class RealmSiteExtensions
{
    public static IApplicationBuilder UseRealmSites(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var links = context.RequestServices.GetRequiredService<RealmSiteLinks>();
        var id = links.RealmId(context);
        if (id is null)
        {
            if (links.Enabled && context.Request.Host.Host.EndsWith("." + links.AdminDomain, StringComparison.OrdinalIgnoreCase))
            { context.Response.StatusCode = 404; return; }
            await next(); return;
        }
        if (context.Request.Query.TryGetValue("realmId", out var requestedIds) &&
            (requestedIds.Count != 1 || requestedIds[0] != id))
        { context.Response.StatusCode = 404; return; }

        var path = context.Request.Path.Value ?? "/";
        if (path == "/") context.Request.Path = "/Realms/" + id;
        else if (path.StartsWith("/Realms/", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(path.TrimEnd('/'), "/Realms/" + id, StringComparison.OrdinalIgnoreCase))
            { context.Response.StatusCode = 404; return; }
        }
        else if (path is not ("/Logout" or "/Error" or "/health") &&
            path != "/realm-map.js" &&
            !path.EndsWith(".css", StringComparison.OrdinalIgnoreCase) &&
            !path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("/images/", StringComparison.OrdinalIgnoreCase))
        {
            // OAuth always begins on its fixed callback host, keeping correlation host-only.
            if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
                context.Response.Redirect(path.Equals("/Login", StringComparison.OrdinalIgnoreCase)
                    ? links.LoginUrl(context) : links.AdminOrigin + path + context.Request.QueryString);
            else context.Response.StatusCode = 404;
            return;
        }
        await next();
    });
}

public sealed class RealmSitePageFilter : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var links = context.HttpContext.RequestServices.GetRequiredService<RealmSiteLinks>();
        // Form/query values must never override the realm selected by the hostname.
        if (links.RealmId(context.HttpContext) is { } id &&
            context.HandlerArguments.TryGetValue("realmId", out var bound) && !string.Equals(bound as string, id, StringComparison.Ordinal))
        { context.Result = new NotFoundResult(); return; }
        await next();
    }
}
