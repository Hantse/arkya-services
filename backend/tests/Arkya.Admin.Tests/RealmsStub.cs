using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arkya.Admin.Realms;

namespace Arkya.Admin.Tests;

internal sealed class RealmsStub : HttpMessageHandler
{
    public RealmSummary Realm { get; set; } = new("terres-aube", "Les Terres d'Aube", "test-cluster", "arkya-realms", "island-test",
        "registry.example/arkya/server@sha256:" + new string('a', 64), 1, "Draft", null, DateTimeOffset.UtcNow,
        [new("area-one", 7777, 0, 1, null), new("area-two", 7778, 0, 1, null)]);
    public RealmDeploymentOptions Options { get; set; } = new([new("test-cluster", "Cluster de test", "arkya-realms")],
        [new("island-test", "Île de départ", "registry.example/arkya/server@sha256:" + new string('a', 64), [new("area-one", 7777), new("area-two", 7778)])], true);
    public List<ContentCall> Calls { get; } = [];
    public HttpStatusCode? WriteError { get; set; }
    public HttpStatusCode? ReadError { get; set; }
    public bool InvalidJson { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Assert.Equal(AdminFactory.RealmsAdminKey, Assert.Single(request.Headers.GetValues("X-Arkya-Admin-Key")));
        Assert.Null(request.Headers.Authorization);
        Assert.False(request.Headers.Contains("Cookie"));
        JsonElement? body = request.Content is null ? null : await request.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var call = new ContentCall(request.Method, request.RequestUri!.AbsolutePath, body);
        Calls.Add(call);
        var error = request.Method == HttpMethod.Get ? ReadError : WriteError;
        if (error.HasValue) return new(error.Value) { Content = new StringContent("private upstream details " + AdminFactory.RealmsAdminKey) };
        if (InvalidJson) return new(HttpStatusCode.OK) { Content = new StringContent("private upstream details " + AdminFactory.RealmsAdminKey) };
        if (request.Method == HttpMethod.Get)
            return call.Path switch
            {
                "/admin/realms" => Json(new[] { Realm }),
                "/admin/realms/options" => Json(Options),
                "/admin/realms/terres-aube" => Json(Realm),
                _ => new(HttpStatusCode.NotFound)
            };
        if (request.Method == HttpMethod.Post && call.Path == "/admin/realms")
        {
            var submitted = body!.Value.Deserialize<CreateRealmRequest>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Realm = Realm with { RealmId = submitted.RealmId, DisplayName = submitted.DisplayName, TargetId = submitted.TargetId, ProfileId = submitted.ProfileId };
            return Json(Realm, HttpStatusCode.Created);
        }
        if (request.Method == HttpMethod.Post && call.Path == "/admin/realms/terres-aube/deploy")
        {
            Realm = Realm with { Revision = Realm.Revision + 1, Status = "Pending" };
            return Json(Realm, HttpStatusCode.Accepted);
        }
        return new(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(value) };
}
