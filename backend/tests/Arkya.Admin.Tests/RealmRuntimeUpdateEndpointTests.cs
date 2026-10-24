using System.Net;
using System.Text.RegularExpressions;
using Arkya.Admin.Realms;

namespace Arkya.Admin.Tests;

public sealed class RealmRuntimeUpdateEndpointTests
{
    private const string Path = "/Realms/terres-aube";
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Maintenance = Path + "?section=maintenance";

    private static AdminFactory Factory()
    {
        var factory = new AdminFactory();
        factory.Settings["RealmsApi:Enabled"] = "true";
        factory.Realms.Realm = factory.Realms.Realm with { Status = "Ready", Revision = 4 };
        factory.Realms.OperationsOptions = factory.Realms.OperationsOptions with
        {
            UpdateVersion = 49, UpdateKind = "RuntimeMigration", CurrentProtocolVersion = 3,
            CurrentBuildId = "arkya-multi-area-v1", TargetProtocolVersion = 5, TargetBuildId = "arkya-versioned-v1",
            TargetGameVersion = "0.1.49", TargetSourceRevision = Commit, TargetSourceRunId = "123456789"
        };
        return factory;
    }

    [Fact]
    public async Task ApprovedRuntimeMigrationShowsTheGameVersionAndProtocolTransition()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Maintenance);
        Assert.Contains("Install approved version 0.1.49.", html);
        Assert.Contains("<dt>Approved game version</dt><dd class=\"mono\">0.1.49</dd>", html);
        Assert.Contains("<dt>Runtime migration</dt><dd>Approved release 49</dd>", html);
        Assert.Contains("<dt>Current runtime</dt><dd>Protocol 3", html);
        Assert.Contains("<dt>Target runtime</dt><dd>Protocol 5", html);
        Assert.Contains("arkya-multi-area-v1", html);
        Assert.Contains("arkya-versioned-v1", html);
        Assert.Contains("Clients must use a compatible build", html);
        Assert.Contains("href=\"https://github.com/Hantse/arkya-mmorpg/commit/" + Commit + "\"", html);
        Assert.Contains("href=\"https://github.com/Hantse/arkya-mmorpg/actions/runs/123456789\"", html);
        Assert.DoesNotContain("No new approved version", html);
        Assert.DoesNotContain("disabled=\"disabled\">Install approved version", html);
        Assert.Equal(factory.Realms.OperationsOptions.UpdateImage, AdminFactory.Input(html, "ExpectedImage"));
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task ManualRuntimeUpdateUsesTheExistingReviewedImageAndRevisionContract()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Maintenance);
        using var response = await browser.PostAsync(Path + "?handler=Update", AdminFactory.Form(new()
        {
            ["__RequestVerificationToken"] = AdminFactory.Input(html, "__RequestVerificationToken"),
            ["ExpectedRevision"] = AdminFactory.Input(html, "ExpectedRevision"),
            ["ExpectedImage"] = AdminFactory.Input(html, "ExpectedImage"),
            ["Reason"] = "Approved migration", ["ConfirmRealmId"] = "terres-aube",
            ["UpdateKind"] = "arbitrary", ["TargetGameVersion"] = "99.0", ["TargetProtocolVersion"] = "99"
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Maintenance, response.Headers.Location!.OriginalString);
        var call = Assert.Single(factory.Realms.Calls, call => call.Method == HttpMethod.Post);
        Assert.Equal("/admin/realms/terres-aube/update", call.Path);
        var body = call.Body!.Value;
        Assert.Equal(3, body.EnumerateObject().Count());
        Assert.Equal(4, body.GetProperty("expectedRevision").GetInt64());
        Assert.Equal(factory.Realms.OperationsOptions.UpdateImage, body.GetProperty("expectedImage").GetString());
        Assert.Equal("Approved migration", body.GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData("ExpectedImage", "")]
    [InlineData("Reason", "")]
    [InlineData("ConfirmRealmId", "another-realm")]
    public async Task RuntimeMigrationKeepsTheDisruptionConfirmationChecks(string field, string value)
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Maintenance);
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AdminFactory.Input(html, "__RequestVerificationToken"),
            ["ExpectedRevision"] = AdminFactory.Input(html, "ExpectedRevision"),
            ["ExpectedImage"] = AdminFactory.Input(html, "ExpectedImage"),
            ["Reason"] = "Approved migration", ["ConfirmRealmId"] = "terres-aube"
        };
        form[field] = value;
        using var response = await browser.PostAsync(Path + "?handler=Update", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("validation-summary-errors", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(factory.Realms.Calls, call => call.Method == HttpMethod.Post);
    }

    [Theory]
    [InlineData("no_approved_update", "A successful build must also be approved in GitOps")]
    [InlineData("runtime_migration_unavailable", "does not match the current realm configuration")]
    [InlineData("deployment_disabled", "Deployment is disabled")]
    [InlineData("target_unavailable", "deployment target is no longer configured")]
    [InlineData("topology_runtime_unsupported", "current profile does not support")]
    [InlineData("operation_in_progress", "current operation finishes")]
    [InlineData("private upstream error", "check this realm's state and approved release configuration")]
    public async Task UnavailableUpdatesExplainWhyNoApprovedReleaseCanBeInstalled(string reason, string message)
    {
        await using var factory = Factory();
        factory.Realms.OperationsOptions = new(true, false, null, null, reason,
            CurrentProtocolVersion: 3, CurrentBuildId: "arkya-multi-area-v1");
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Maintenance);
        Assert.Contains(message, WebUtility.HtmlDecode(html));
        Assert.Contains("No new approved version is available", html);
        Assert.Contains("Current runtime: protocol 3", html);
        Assert.Contains("disabled=\"disabled\">Install approved version", html);
        Assert.DoesNotContain("Approved game version", html);
        Assert.DoesNotContain("0.1.49", html);
        Assert.DoesNotContain("private upstream error", html);
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task LegacyApprovalDoesNotInventAGameVersionAndBlockedApprovalsKeepTheirReason()
    {
        await using var factory = Factory();
        factory.Realms.OperationsOptions = new(true, true, factory.Realms.OperationsOptions.UpdateImage, 49, null);
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Maintenance);
        Assert.Contains("<dt>Image update</dt><dd>Approved release 49</dd>", html);
        Assert.Contains("Game version metadata is not available", html);
        Assert.DoesNotContain("0.1.49", html);
        factory.Realms.OperationsOptions = factory.Realms.OperationsOptions with { CanUpdate = false, UnavailableReason = "operation_in_progress" };
        html = await browser.GetStringAsync(Maintenance);
        Assert.Contains("current operation finishes", html);
        Assert.Contains("disabled=\"disabled\">Install approved version", html);
    }

    [Fact]
    public async Task RuntimeApprovalMetadataIsEscapedAndCannotCreateArbitrarySourceLinks()
    {
        await using var factory = Factory();
        factory.Realms.OperationsOptions = factory.Realms.OperationsOptions with
        {
            TargetGameVersion = "<script>version</script>", TargetBuildId = "<img src=x>",
            TargetSourceRevision = "../evil", TargetSourceRunId = "123\" onclick=\"evil"
        };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Maintenance);
        Assert.Contains("&lt;script&gt;version&lt;/script&gt;", html);
        Assert.Contains("&lt;img src=x&gt;", html);
        Assert.DoesNotContain("<script>version", html);
        Assert.DoesNotContain("<img src=x>", html);
        Assert.DoesNotContain("/commit/", html);
        Assert.DoesNotContain("/actions/runs/", html);
        Assert.DoesNotContain("onclick=\"evil", html);
    }

    [Fact]
    public async Task RuntimeMigrationsAreLabelledAsSuchInTheOverviewAndHistory()
    {
        await using var factory = Factory();
        var now = DateTimeOffset.UtcNow;
        factory.Realms.Realm = factory.Realms.Realm with { Operations = [new("RuntimeMigration", 4, "admin:operator", "Approved migration", now, now,
            "Ready", null, "previous-image", "next-image")] };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var overview = await browser.GetStringAsync(Path);
        Assert.Contains("Last operation: <strong>Runtime migration</strong>", overview);
        var history = await browser.GetStringAsync(Path + "?section=history");
        Assert.Contains("<th scope=\"row\">Runtime migration<small>", history);
        Assert.DoesNotContain("<th scope=\"row\">Restart", history);
    }
}
