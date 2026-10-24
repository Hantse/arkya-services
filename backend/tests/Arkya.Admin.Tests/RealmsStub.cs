using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arkya.Admin.Realms;
using Arkya.ServiceDefaults;

namespace Arkya.Admin.Tests;

internal sealed class RealmsStub : HttpMessageHandler
{
    public RealmSummary Realm { get; set; } = new("terres-aube", "Les Terres d'Aube", "test-cluster", "arkya-realms", "island-test",
        "registry.example/arkya/server@sha256:" + new string('a', 64), 1, "Draft", null, DateTimeOffset.UtcNow,
        [new("area-one", 7777, 0, 1, null), new("area-two", 7778, 0, 1, null)]);
    public RealmDeploymentOptions Options { get; set; } = new([new("test-cluster", "Cluster de test", "arkya-realms")],
        [new("island-test", "Île de départ", "registry.example/arkya/server@sha256:" + new string('a', 64), [new("area-one", 7777), new("area-two", 7778)])], true);
    public List<ContentCall> Calls { get; } = [];
    public List<string?> Actors { get; } = [];
    public RealmOperationsOptions OperationsOptions { get; set; } = new(true, true,
        "registry.example/arkya/server@sha256:" + new string('b', 64), 2, null);
    public RealmCommands Commands { get; set; } = new([new("area-one", DateTimeOffset.UtcNow, true, ["announce", "kick", "save"])], []);
    public RealmServerVersion[] ServerVersions { get; set; } = [];
    public RealmWorldMap WorldMap { get; set; } = new("terres-aube", DateTimeOffset.UtcNow, 2, 10, []);
    public HttpStatusCode? WorldMapReadError { get; set; }
    public bool WorldMapInvalidJson { get; set; }
    public RealmConfiguration Configuration { get; set; } = new(0, 1, 1, null, null, []);
    public HttpStatusCode? ConfigurationReadError { get; set; }
    public HttpStatusCode? CommandsReadError { get; set; }
    public HttpStatusCode? ServerVersionsReadError { get; set; }
    public bool ServerVersionsInvalidJson { get; set; }
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
        Actors.Add(request.Headers.TryGetValues("X-Arkya-Admin-Actor", out var actors) ? Assert.Single(actors) : null);
        var error = request.Method == HttpMethod.Get ? ReadError : WriteError;
        if (error.HasValue) return new(error.Value) { Content = new StringContent("private upstream details " + AdminFactory.RealmsAdminKey) };
        if (InvalidJson) return new(HttpStatusCode.OK) { Content = new StringContent("private upstream details " + AdminFactory.RealmsAdminKey) };
        if (request.Method == HttpMethod.Get)
        {
            if (call.Path.EndsWith("/configuration") && ConfigurationReadError is { } configurationError)
                return new(configurationError) { Content = new StringContent("private upstream details " + AdminFactory.RealmsAdminKey) };
            if (call.Path.EndsWith("/commands") && CommandsReadError is { } commandsError)
                return new(commandsError) { Content = new StringContent("private upstream details " + AdminFactory.RealmsAdminKey) };
            if (call.Path.EndsWith("/server-versions"))
            {
                if (ServerVersionsReadError is { } versionsError)
                    return new(versionsError) { Content = new StringContent("private upstream details " + AdminFactory.RealmsAdminKey) };
                if (ServerVersionsInvalidJson) return new(HttpStatusCode.OK) { Content = new StringContent("invalid upstream response") };
            }
            if (call.Path.EndsWith("/world-map"))
            {
                if (WorldMapReadError is { } mapError)
                    return new(mapError) { Content = new StringContent("private upstream details " + AdminFactory.RealmsAdminKey) };
                if (WorldMapInvalidJson) return new(HttpStatusCode.OK) { Content = new StringContent("invalid upstream response") };
            }
            return call.Path switch
            {
                "/admin/realms" => Json(new[] { Realm }),
                "/admin/realms/options" => Json(Options),
                "/admin/realms/terres-aube" => Json(Realm),
                "/admin/realms/terres-aube/operations-options" => Json(OperationsOptions),
                "/admin/realms/terres-aube/commands" => Json(Commands),
                "/admin/realms/terres-aube/server-versions" => Json(ServerVersions),
                "/admin/realms/terres-aube/world-map" => Json(WorldMap),
                "/admin/realms/terres-aube/configuration" => Json(Configuration),
                _ => new(HttpStatusCode.NotFound)
            };
        }
        if (request.Method == HttpMethod.Put && call.Path == "/admin/realms/terres-aube/configuration")
        {
            var submitted = body!.Value.Deserialize<UpdateRealmConfiguration>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            if (submitted.ExpectedRevision != Configuration.Revision) return new(HttpStatusCode.Conflict);
            var now = DateTimeOffset.UtcNow;
            var change = new RealmConfigurationChange(Configuration.Revision + 1, Configuration.Revision, submitted.XpMultiplier,
                submitted.HarvestMultiplier, Configuration.XpMultiplier, Configuration.HarvestMultiplier, now, Actors[^1]!, submitted.Reason);
            Configuration = new(change.Revision, submitted.XpMultiplier, submitted.HarvestMultiplier, now, Actors[^1]!, [.. Configuration.History, change]);
            return Json(Configuration);
        }
        if (request.Method == HttpMethod.Post && call.Path == "/admin/realms")
        {
            var submitted = body!.Value.Deserialize<CreateRealmRequest>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Realm = Realm with { RealmId = submitted.RealmId, DisplayName = submitted.DisplayName, TargetId = submitted.TargetId, ProfileId = submitted.ProfileId, Topology = submitted.Topology };
            return Json(Realm, HttpStatusCode.Created);
        }
        if (request.Method == HttpMethod.Put && call.Path == "/admin/realms/terres-aube/topology")
        {
            var submitted = body!.Value;
            if (Realm.Status != "Draft" || submitted.GetProperty("expectedRevision").GetInt64() != Realm.Revision)
                return new(HttpStatusCode.Conflict);
            Realm = Realm with { Revision = Realm.Revision + 1, ProfileId = submitted.GetProperty("profileId").GetString()!,
                Topology = submitted.GetProperty("topology").Deserialize<RealmTopology>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
            return Json(Realm);
        }
        if (request.Method == HttpMethod.Post && call.Path == "/admin/realms/terres-aube/deploy")
        {
            Realm = Realm with { Revision = Realm.Revision + 1, Status = "Pending" };
            return Json(Realm, HttpStatusCode.Accepted);
        }
        if (request.Method == HttpMethod.Post && (call.Path.EndsWith("/restart") || call.Path.EndsWith("/update")))
        {
            Realm = Realm with { Revision = Realm.Revision + 1, Status = "Pending" };
            return Json(Realm, HttpStatusCode.Accepted);
        }
        if (request.Method == HttpMethod.Post && call.Path == "/admin/realms/terres-aube/delete")
        {
            var submitted = body!.Value;
            if (submitted.GetProperty("confirmRealmId").GetString() != Realm.RealmId) return new(HttpStatusCode.BadRequest);
            if (submitted.GetProperty("expectedRevision").GetInt64() != Realm.Revision || !RealmPresentation.CanDelete(Realm))
                return new(HttpStatusCode.Conflict);
            Realm = Realm with { Revision = Realm.Revision + 1, Status = "Deleting" };
            return Json(Realm, HttpStatusCode.Accepted);
        }
        if (request.Method == HttpMethod.Post && call.Path == "/admin/realms/terres-aube/commands")
        {
            var submitted = body!.Value.Deserialize<RealmCommandRequest>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            var command = new RealmCommand(submitted.RequestId, submitted.ServerId, submitted.Type, submitted.Message,
                submitted.CharacterId, "Pending", Actors[^1]!, submitted.Reason, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(2), null, null, null);
            Commands = Commands with { Commands = [command, .. Commands.Commands] };
            return Json(command, HttpStatusCode.Accepted);
        }
        return new(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(value) };
}
