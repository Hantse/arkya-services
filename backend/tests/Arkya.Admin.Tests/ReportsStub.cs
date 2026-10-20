using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arkya.ServiceDefaults.Reports;

namespace Arkya.Admin.Tests;

internal sealed record ReportCall(HttpMethod Method, Uri Uri, JsonElement? Body, string? Actor);

internal sealed class ReportsStub : HttpMessageHandler
{
    public static readonly Guid Id = Guid.Parse("3352f8c9-0cb6-453c-bf92-e2db0ff4fcfa");
    public BugReportResponse Report { get; set; } = new(1, Id, Guid.NewGuid(), Guid.NewGuid(),
        "Bloqué dans une souche", "Le personnage ne peut plus avancer.\nPrès du rivage.", "0.1.0", "StarterIsland",
        "WebGL", BugReportStatuses.New, ["urgent"], "", 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
        new(1, 1, 68, "image/png", new string('a', 64)), []);
    public byte[] Screenshot { get; set; } = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aZc8AAAAASUVORK5CYII=");
    public string ScreenshotContentType { get; set; } = "image/png";
    public long Total { get; set; } = 1;
    public bool Empty { get; set; }
    public List<ReportCall> Calls { get; } = [];
    public HttpStatusCode? WriteError { get; set; }
    public HttpStatusCode? ReadError { get; set; }
    public bool InvalidJson { get; set; }
    public bool OmitKind { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Assert.Equal(AdminFactory.ReportsAdminKey, Assert.Single(request.Headers.GetValues("X-Arkya-Reports-Admin-Key")));
        Assert.Null(request.Headers.Authorization);
        Assert.False(request.Headers.Contains("Cookie"));
        Assert.False(request.Headers.Contains("X-Arkya-Service-Key"));
        Assert.False(request.Headers.Contains("X-Arkya-Admin-Key"));
        JsonElement? body = request.Content is null ? null : await request.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var call = new ReportCall(request.Method, request.RequestUri!, body,
            request.Headers.TryGetValues("X-Arkya-Admin-Actor", out var actors) ? actors.Single() : null);
        Calls.Add(call);
        var error = request.Method == HttpMethod.Get ? ReadError : WriteError;
        if (error.HasValue) return new(error.Value) { Content = new StringContent("private upstream details " + AdminFactory.ReportsAdminKey) };
        if (InvalidJson) return new(HttpStatusCode.OK) { Content = new StringContent("private upstream details " + AdminFactory.ReportsAdminKey) };
        if (request.Method == HttpMethod.Get)
        {
            if (call.Uri.AbsolutePath == "/internal/bug-reports")
            {
                var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(call.Uri.Query);
                return Json(new BugReportListResponse(Empty ? [] : [Report], int.Parse(query["offset"].ToString()),
                    int.Parse(query["limit"].ToString()), Total));
            }
            if (call.Uri.AbsolutePath == $"/internal/bug-reports/{Id:D}") return Json(Report);
            if (call.Uri.AbsolutePath == $"/internal/bug-reports/{Id:D}/screenshot")
            {
                var content = new ByteArrayContent(Screenshot);
                content.Headers.ContentType = new MediaTypeHeaderValue(ScreenshotContentType);
                return new(HttpStatusCode.OK) { Content = content };
            }
        }
        if (request.Method == HttpMethod.Patch && call.Uri.AbsolutePath == $"/internal/bug-reports/{Id:D}")
        {
            var submitted = body!.Value.Deserialize<UpdateBugReportRequest>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Report = Report with { Status = submitted.Status!, Tags = submitted.Tags!, Resolution = submitted.Resolution!, Revision = Report.Revision + 1 };
            return Json(Report);
        }
        return new(HttpStatusCode.NotFound);
    }

    private HttpResponseMessage Json<T>(T value)
    {
        if (!OmitKind) return new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
        var json = JsonSerializer.SerializeToNode(value, JsonSerializerOptions.Web)!;
        if (value is BugReportResponse) json.AsObject().Remove("kind");
        if (value is BugReportListResponse)
            foreach (var item in json["items"]!.AsArray().OfType<JsonObject>()) item.Remove("kind");
        return new(HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), System.Text.Encoding.UTF8, "application/json") };
    }
}
