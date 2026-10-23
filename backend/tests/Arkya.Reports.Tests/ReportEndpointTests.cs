using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Arkya.Reports.Api.Domain;
using Arkya.ServiceDefaults.Reports;

namespace Arkya.Reports.Tests;

public sealed class ReportEndpointTests
{
    [Theory]
    [InlineData(null, 401)]
    [InlineData("invalid", 401)]
    [InlineData("down", 503)]
    public async Task SubmissionRequiresLiveIdentity(string? token, int expected)
    {
        using var factory = new ReportsFactory();
        using var client = factory.Client(token);
        Assert.Equal(expected, (int)(await client.PostAsJsonAsync("/v1/bug-reports", Fixtures.Request())).StatusCode);
    }

    [Fact]
    public async Task PlayerOwnsReportAndCannotReadOrMutateOtherAccounts()
    {
        using var factory = new ReportsFactory();
        using var alice = factory.Client("alice");
        using var bob = factory.Client("bob");
        using var anonymous = factory.Client();
        var submitted = await alice.PostAsJsonAsync("/v1/bug-reports", Fixtures.Request());
        Assert.Equal(HttpStatusCode.Created, submitted.StatusCode);
        var report = (await submitted.Content.ReadFromJsonAsync<BugReportResponse>())!;
        Assert.Equal(StubSessions.Alice, report.AccountId);
        Assert.Equal("new", report.Status);
        Assert.Equal(1, report.Revision);
        Assert.Empty(report.Tags);
        Assert.Empty(report.History);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/v1/bug-reports/{report.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/v1/bug-reports/{report.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/v1/bug-reports")).StatusCode);
        Assert.Empty((await bob.GetFromJsonAsync<BugReportListResponse>("/v1/bug-reports"))!.Items);
        Assert.Single((await alice.GetFromJsonAsync<BugReportListResponse>("/v1/bug-reports"))!.Items);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync($"/internal/bug-reports/{report.Id}/screenshot")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.PatchAsJsonAsync($"/internal/bug-reports/{report.Id}",
            new UpdateBugReportRequest(1, "resolved", ["urgent"], "fixed"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/v1/bug-reports/{report.Id}/screenshot")).StatusCode);
        Assert.DoesNotContain("screenshotBase64", await submitted.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RetryIsIdempotentButChangingContentConflictsAndIdsAreScopedToAccount()
    {
        using var factory = new ReportsFactory();
        using var alice = factory.Client("alice");
        using var bob = factory.Client("bob");
        var request = Fixtures.Request();
        var first = await alice.PostAsJsonAsync("/v1/bug-reports", request);
        var replay = await alice.PostAsJsonAsync("/v1/bug-reports", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal((await first.Content.ReadFromJsonAsync<BugReportResponse>())!.Id,
            (await replay.Content.ReadFromJsonAsync<BugReportResponse>())!.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await alice.PostAsJsonAsync("/v1/bug-reports", request with { Title = "different" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await bob.PostAsJsonAsync("/v1/bug-reports", request)).StatusCode);
    }

    [Fact]
    public async Task AdminFiltersUpdatesWithRevisionAndReadsProtectedPng()
    {
        using var factory = new ReportsFactory();
        using var player = factory.Client("alice");
        using var admin = factory.Client(admin: true);
        admin.DefaultRequestHeaders.Add("X-Arkya-Admin-Actor", "admin:discord:1234");
        var creation = await player.PostAsJsonAsync("/v1/bug-reports", Fixtures.Request());
        var report = (await creation.Content.ReadFromJsonAsync<BugReportResponse>())!;
        var update = new UpdateBugReportRequest(report.Revision, "in_progress", [" Urgent ", "urgent", "collision"], "Investigating");
        var response = await admin.PatchAsJsonAsync($"/internal/bug-reports/{report.Id}", update);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var changed = (await response.Content.ReadFromJsonAsync<BugReportResponse>())!;
        Assert.Equal(2, changed.Revision);
        Assert.Equal(["urgent", "collision"], changed.Tags);
        Assert.Equal("admin:discord:1234", Assert.Single(changed.History).Actor);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PatchAsJsonAsync($"/internal/bug-reports/{report.Id}", update)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PatchAsJsonAsync($"/internal/bug-reports/{Guid.NewGuid()}", update)).StatusCode);
        var list = (await admin.GetFromJsonAsync<BugReportListResponse>("/internal/bug-reports?tag=URGENT&status=in_progress&offset=0&limit=1"))!;
        Assert.Equal(1, list.Total);
        Assert.Equal(report.Id, Assert.Single(list.Items).Id);
        Assert.Empty(list.Items[0].History);
        Assert.Empty((await admin.GetFromJsonAsync<BugReportListResponse>("/internal/bug-reports?status=new"))!.Items);
        var own = (await player.GetFromJsonAsync<BugReportResponse>($"/v1/bug-reports/{report.Id}"))!;
        Assert.Equal("in_progress", own.Status);
        Assert.Empty(own.History);
        var screenshot = await admin.GetAsync($"/internal/bug-reports/{report.Id}/screenshot");
        Assert.Equal("image/png", screenshot.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Fixtures.Png(), await screenshot.Content.ReadAsByteArrayAsync());
        Assert.Contains("no-store", screenshot.Headers.CacheControl!.ToString());
        Assert.Equal("nosniff", screenshot.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Theory]
    [InlineData("accountId", "\"11111111-1111-1111-1111-111111111111\"")]
    [InlineData("status", "\"resolved\"")]
    [InlineData("tags", "[\"urgent\"]")]
    [InlineData("unexpected", "true")]
    public async Task PlayerCannotInjectServerOwnedOrUnknownFields(string field, string value)
    {
        using var factory = new ReportsFactory();
        using var client = factory.Client("alice");
        var json = JsonSerializer.Serialize(Fixtures.Request(), JsonSerializerOptions.Web);
        json = json[..^1] + $",\"{field}\":{value}}}";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/v1/bug-reports",
            new StringContent(json, Encoding.UTF8, "application/json"))).StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{invalid}")]
    public async Task MissingOrMalformedBodyFails(string json)
    {
        using var factory = new ReportsFactory();
        using var client = factory.Client("alice");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/v1/bug-reports",
            new StringContent(json, Encoding.UTF8, "application/json"))).StatusCode);
    }

    [Fact]
    public async Task OversizedBodyAndScreenshotAreRejected()
    {
        using var factory = new ReportsFactory();
        using var client = factory.Client("alice");
        var large = Fixtures.Request() with { ScreenshotBase64 = new string('A', PngScreenshot.MaximumBase64Characters + 4) };
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsJsonAsync("/v1/bug-reports", large)).StatusCode);
        using var body = new StringContent(new string('A', (int)Arkya.Reports.Api.Program.MaximumRequestBodyBytes + 1),
            Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsync("/v1/bug-reports", body)).StatusCode);
    }

    [Fact]
    public async Task ChunkedUploadsHaveTheSameBodyLimit()
    {
        using var factory = new ReportsFactory();
        using var client = factory.Client("alice");
        using var body = new UnknownLengthJson("{\"screenshotBase64\":\"" +
            new string('A', (int)Arkya.Reports.Api.Program.MaximumRequestBodyBytes + 1) + "\"}");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsync("/v1/bug-reports", body)).StatusCode);
    }

    [Theory]
    [InlineData("offset=-1")]
    [InlineData("offset=100001")]
    [InlineData("limit=101")]
    [InlineData("limit=0")]
    [InlineData("limit=1&limit=2")]
    [InlineData("status=unknown")]
    [InlineData("tag=bad%20tag")]
    [InlineData("accountId=spoof")]
    public async Task QueryValidationIsBoundedAndStrict(string query)
    {
        using var factory = new ReportsFactory();
        using var client = factory.Client(admin: true);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/internal/bug-reports?" + query)).StatusCode);
    }

    [Theory]
    [InlineData(BugReportKinds.Bug)]
    [InlineData(BugReportKinds.Suggestion)]
    public async Task SubmissionsAndRetriesHaveNoPerAccountQuota(string kind)
    {
        using var factory = new ReportsFactory();
        using var alice = factory.Client("alice");
        for (var i = 0; i < 8; i++)
        {
            var request = Fixtures.Request() with { Kind = kind };
            using var created = await alice.PostAsJsonAsync("/v1/bug-reports", request);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using var replay = await alice.PostAsJsonAsync("/v1/bug-reports", request);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        }
        Assert.Equal(8, (await alice.GetFromJsonAsync<BugReportListResponse>("/v1/bug-reports"))!.Total);
    }

    [Fact]
    public async Task ProductionRejectsPlainHttpAndServiceKeyCannotAdminister()
    {
        using var factory = new ReportsFactory(production: true);
        using var plain = factory.Client(admin: true, https: false);
        Assert.Equal(HttpStatusCode.BadRequest, (await plain.GetAsync("/internal/bug-reports")).StatusCode);
        using var service = factory.Client();
        service.DefaultRequestHeaders.Add("X-Arkya-Reports-Admin-Key", ReportsFactory.ServiceKey);
        Assert.Equal(HttpStatusCode.Forbidden, (await service.GetAsync("/internal/bug-reports")).StatusCode);
    }

    [Fact]
    public async Task CorsAllowsConfiguredWebsiteButNeverAdministratorHeaders()
    {
        using var factory = new ReportsFactory();
        using var client = factory.Client();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/v1/bug-reports");
        request.Headers.Add("Origin", "https://arkya.example");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        var response = await client.SendAsync(request);
        Assert.Equal("https://arkya.example", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.DoesNotContain("X-Arkya-Reports-Admin-Key", string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers")));
    }

    [MongoFact]
    public async Task ReadinessRequiresAWorkingMongoConnection()
    {
        using var factory = new ReportsFactory();
        factory.Settings["Mongo:ConnectionString"] = Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO");
        using var client = factory.Client();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task ReadinessFailsClosedWhenMongoIsUnavailableButLivenessRemainsHealthy()
    {
        using var factory = new ReportsFactory();
        factory.Settings["Mongo:ConnectionString"] = "mongodb://127.0.0.1:1";
        using var client = factory.Client();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
    }

    private sealed class UnknownLengthJson : HttpContent
    {
        private readonly byte[] bytes;
        public UnknownLengthJson(string json)
        {
            bytes = Encoding.UTF8.GetBytes(json);
            Headers.ContentType = new("application/json");
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }
}
