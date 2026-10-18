using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;

// Test-only orchestration. Production services have no seeded-login or test-auth endpoint.
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
if (args.Length < 2) { Console.Error.WriteLine("Use prepare|characters|tickets|verify-state|verify-revocation|cleanup <private-directory>."); return 2; }
var action = args[0];
var directory = Path.GetFullPath(args[1]);
var manifestPath = Path.Combine(directory, "fixture.json");
try
{
    Fixture fixture;
    if (action == "prepare")
    {
        if (File.Exists(manifestPath)) throw new InvalidOperationException("Fixture already exists.");
        var mongo = Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO");
        if (string.IsNullOrWhiteSpace(mongo)) throw new InvalidOperationException("ARKYA_TEST_MONGO must reference an isolated test Mongo server.");
        Directory.CreateDirectory(directory);
        fixture = new Fixture { Mongo = mongo, Prefix = "arkya_web_beta_test_" + Guid.NewGuid().ToString("N"),
            IdentityBaseUrl = Origin(), CharactersBaseUrl = Origin(), RealmsBaseUrl = Origin(),
            Endpoint = Environment.GetEnvironmentVariable("ARKYA_TEST_GAME_ENDPOINT") ?? "ws://127.0.0.1:17943/",
            SessionToken = Secret(), AccountId = Guid.NewGuid().ToString("D"), DedicatedKey = Secret() };
        var identityKey = Secret(); var realmKey = Secret();
        Dictionary<string, string> Common(string database, string origin) => new()
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development", ["ASPNETCORE_URLS"] = origin,
            ["Mongo__ConnectionString"] = mongo, ["Mongo__DatabaseName"] = fixture.Prefix + database,
            ["Logging__LogLevel__Default"] = "Warning", ["Logging__LogLevel__Microsoft.AspNetCore"] = "Warning",
            ["Logging__LogLevel__System.Net.Http.HttpClient"] = "None", ["AllowedHosts"] = "127.0.0.1;localhost"
        };
        var identity = Common("_identity", fixture.IdentityBaseUrl);
        identity["ServiceAuthentication__Key"] = identityKey;
        identity["WebClient__AllowedOrigins__0"] = "http://127.0.0.1:51806";
        identity["Discord__ClientId"] = "fixture-no-oauth"; identity["Discord__ClientSecret"] = Secret();
        identity["Discord__PublicBaseUrl"] = "https://localhost:7141";
        identity["Discord__RedirectUri"] = "https://localhost:7141/v1/auth/discord/callback";
        var realms = Common("_realms", fixture.RealmsBaseUrl);
        realms["RealmsAdmin__Key"] = Secret(); realms["Deployment__Enabled"] = "false";
        realms["GameAccess__ServiceKey"] = realmKey;
        realms["GameAccess__StandaloneEntries__0__RealmId"] = fixture.RealmId;
        realms["GameAccess__StandaloneEntries__0__DisplayName"] = "Real local beta fixture";
        realms["GameAccess__StandaloneEntries__0__ServerId"] = fixture.ServerId;
        realms["GameAccess__StandaloneEntries__0__Endpoint"] = fixture.Endpoint;
        realms["GameAccess__StandaloneEntries__0__Enabled"] = "true";
        var characters = Common("_characters", fixture.CharactersBaseUrl);
        characters["ServiceAuthentication__Key"] = Secret();
        characters["Identity__BaseUrl"] = fixture.IdentityBaseUrl; characters["Identity__ServiceKey"] = identityKey;
        characters["Realms__BaseUrl"] = fixture.RealmsBaseUrl; characters["Realms__ServiceKey"] = realmKey;
        characters["GameAccess__DedicatedServers__0__RealmId"] = fixture.RealmId;
        characters["GameAccess__DedicatedServers__0__ServerId"] = fixture.ServerId;
        characters["GameAccess__DedicatedServers__0__Key"] = fixture.DedicatedKey;
        characters["WebClient__AllowedOrigins__0"] = "http://127.0.0.1:51806";
        fixture.Services = new() { ["Arkya.Identity.Api"] = identity, ["Arkya.Realms.Api"] = realms, ["Arkya.Characters.Api"] = characters };
        // Persist before touching Mongo so cleanup remains possible after an interrupted startup.
        await WriteFixture();
        var client = Mongo(fixture.Mongo);
        var db = client.GetDatabase(fixture.Prefix + "_identity");
        var now = DateTime.UtcNow;
        await db.GetCollection<BsonDocument>("accounts").InsertOneAsync(new BsonDocument
        { ["_id"] = fixture.AccountId, ["DiscordId"] = "fixture-" + Guid.NewGuid().ToString("N"), ["DisplayName"] = "Web beta fixture", ["CreatedAt"] = now, ["LastLoginAt"] = now });
        await db.GetCollection<BsonDocument>("sessions").InsertOneAsync(new BsonDocument
        { ["_id"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fixture.SessionToken))), ["AccountId"] = fixture.AccountId,
          ["DisplayName"] = "Web beta fixture", ["ExpiresAt"] = now.AddHours(2) });
        Console.WriteLine(manifestPath);
    }
    else
    {
        fixture = JsonSerializer.Deserialize<Fixture>(await File.ReadAllTextAsync(manifestPath), json)!;
        if (!fixture.Prefix.StartsWith("arkya_web_beta_test_", StringComparison.Ordinal) || fixture.Prefix.Length != 52)
            throw new InvalidOperationException("Refusing a fixture without an isolated test database prefix.");
        if (action == "verify-state")
        {
            if (fixture.Characters.Length != 2) throw new InvalidDataException("State verification requires the two fixture characters.");
            var collection = Mongo(fixture.Mongo).GetDatabase(fixture.Prefix + "_characters").GetCollection<BsonDocument>("game_leases");
            var summaries = new List<object>();
            foreach (var character in fixture.Characters)
            {
                var id = character.GetProperty("id").GetString();
                var record = await collection.Find(new BsonDocument("_id", id)).Project(Builders<BsonDocument>.Projection
                    .Include("State").Include("AuthorityEpoch").Include("LeaseId").Include("LeaseExpiresAt")).SingleOrDefaultAsync();
                if (record is null || !record.GetValue("State", BsonNull.Value).IsBsonDocument)
                    throw new InvalidDataException("A fixture character has no persisted state.");
                var state = record["State"].AsBsonDocument;
                var position = state["Position"].AsBsonDocument;
                var revision = state["Revision"].ToInt64();
                var epoch = record["AuthorityEpoch"].ToInt64();
                var x = position["X"].ToDouble(); var y = position["Y"].ToDouble(); var z = position["Z"].ToDouble();
                if (revision < 1 || epoch < 1 || !double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
                    throw new InvalidDataException("A fixture state has invalid revision, epoch or coordinates.");
                if (!Guid.TryParse(record["LeaseId"].AsString, out var leaseId)) throw new InvalidDataException("A fixture lease has an invalid identity.");
                var activeLease = leaseId != Guid.Empty && record["LeaseExpiresAt"].ToUniversalTime() > DateTime.UtcNow;
                if (activeLease) throw new InvalidDataException("A fixture character still has an active lease.");
                summaries.Add(new { index = summaries.Count + 1, revision, authorityEpoch = epoch, position = new { x, y, z }, activeLease });
            }
            // Deliberately omit database names, account/character IDs, keys and ticket hashes.
            Console.WriteLine(JsonSerializer.Serialize(new { verifiedCount = summaries.Count, states = summaries }, json));
        }
        else if (action == "cleanup")
        {
            var client = Mongo(fixture.Mongo);
            foreach (var suffix in new[] { "_identity", "_characters", "_realms" }) await client.DropDatabaseAsync(fixture.Prefix + suffix);
            foreach (var file in new[] { "fixture.json", "tickets.json" }) if (File.Exists(Path.Combine(directory, file))) File.Delete(Path.Combine(directory, file));
            Console.WriteLine("Isolated test sessions and databases removed.");
        }
        else
        {
            using var http = new HttpClient { BaseAddress = new Uri(fixture.CharactersBaseUrl), Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.SessionToken);
            if (action == "characters")
            {
                var values = new List<JsonElement>();
                foreach (var name in new[] { "Aelys", "Beran" })
                {
                    var result = await http.PostAsJsonAsync("/v1/characters", new { name });
                    Require(result, "Character creation");
                    values.Add(await result.Content.ReadFromJsonAsync<JsonElement>());
                }
                fixture.Characters = values.ToArray(); await WriteFixture();
                Console.WriteLine("Two characters created through the real authenticated API.");
            }
            else if (action == "tickets")
            {
                var clients = new List<object>();
                foreach (var character in fixture.Characters)
                {
                    var id = character.GetProperty("id").GetString();
                    var result = await http.PostAsJsonAsync($"/v1/characters/{id}/admissions", new
                    { realmId = fixture.RealmId, protocolVersion = 1, buildId = "arkya-web-beta-v1" });
                    Require(result, "Admission");
                    var admission = await result.Content.ReadFromJsonAsync<JsonElement>();
                    var current = await http.GetAsync($"/v1/characters/{id}"); Require(current, "Character read");
                    clients.Add(new { character = await current.Content.ReadFromJsonAsync<JsonElement>(), admission });
                }
                var output = Path.Combine(directory, "tickets.json");
                await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { fixture.CharactersBaseUrl, fixture.DedicatedKey,
                    fixture.RealmId, fixture.ServerId, fixture.Endpoint, clients }, json));
                Console.WriteLine(output);
            }
            else if (action == "verify-revocation")
            {
                if (fixture.Characters.Length != 2) throw new InvalidDataException("Revocation test requires two fixture characters.");
                var admissions = new List<JsonElement>();
                foreach (var character in fixture.Characters)
                {
                    using var issued = await http.PostAsJsonAsync($"/v1/characters/{character.GetProperty("id").GetString()}/admissions",
                        new { realmId = fixture.RealmId, protocolVersion = 1, buildId = "arkya-web-beta-v1" });
                    Require(issued, "Admission before revocation");
                    admissions.Add(await issued.Content.ReadFromJsonAsync<JsonElement>());
                }
                using var dedicated = new HttpClient { BaseAddress = new Uri(fixture.CharactersBaseUrl), Timeout = TimeSpan.FromSeconds(15) };
                dedicated.DefaultRequestHeaders.Add("X-Arkya-Dedicated-Key", fixture.DedicatedKey);
                using var consumed = await dedicated.PostAsJsonAsync("/internal/game/admissions/consume", new { ticket = admissions[0].GetProperty("ticket").GetString() });
                Require(consumed, "Consume before revocation");
                var lease = await consumed.Content.ReadFromJsonAsync<JsonElement>();
                var leaseBody = new { leaseId = lease.GetProperty("leaseId").GetString(), authorityEpoch = lease.GetProperty("authorityEpoch").GetInt64() };
                var id = fixture.Characters[0].GetProperty("id").GetString();
                var route = $"/internal/game/leases/{id}";
                var saveBody = new { leaseBody.leaseId, leaseBody.authorityEpoch, expectedRevision = 0, position = new { x = 10, y = 31, z = 20 }, forward = new { x = 0, z = 1 } };
                using var saved = await dedicated.PutAsJsonAsync(route + "/state", saveBody); Require(saved, "Save before revocation");
                using var renewed = await dedicated.PostAsJsonAsync(route + "/heartbeat", leaseBody); Require(renewed, "Heartbeat before revocation");
                var collection = Mongo(fixture.Mongo).GetDatabase(fixture.Prefix + "_characters").GetCollection<BsonDocument>("game_leases");
                var stored = await collection.Find(new BsonDocument("_id", id)).SingleAsync();
                var reference = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fixture.SessionToken)));
                if (stored["SessionReference"].AsString != reference || stored.ToJson().Contains(fixture.SessionToken, StringComparison.Ordinal))
                    throw new InvalidDataException("The lease did not retain only the hashed session reference.");
                using var identity = new HttpClient { BaseAddress = new Uri(fixture.IdentityBaseUrl), Timeout = TimeSpan.FromSeconds(15) };
                identity.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.SessionToken);
                using var logout = await identity.PostAsync("/v1/auth/logout", null); Expect(logout, HttpStatusCode.NoContent);
                using var rejectedTicket = await dedicated.PostAsJsonAsync("/internal/game/admissions/consume", new { ticket = admissions[1].GetProperty("ticket").GetString() });
                Expect(rejectedTicket, HttpStatusCode.Conflict);
                using var rejectedHeartbeat = await dedicated.PostAsJsonAsync(route + "/heartbeat", leaseBody); Expect(rejectedHeartbeat, HttpStatusCode.Conflict);
                using var rejectedSave = await dedicated.PutAsJsonAsync(route + "/state", saveBody); Expect(rejectedSave, HttpStatusCode.Conflict);
                using var rejectedPublic = await http.GetAsync("/v1/realms"); Expect(rejectedPublic, HttpStatusCode.Unauthorized);
                using var released = await dedicated.PostAsJsonAsync(route + "/release", leaseBody); Expect(released, HttpStatusCode.NoContent);
                stored = await collection.Find(new BsonDocument("_id", id)).SingleAsync();
                if (stored["LeaseId"].AsString != Guid.Empty.ToString("D") || stored["State"]["Revision"].ToInt64() != 1)
                    throw new InvalidDataException("Revocation changed saved state or prevented lease release.");
                Console.WriteLine("Real services + Mongo: hashed session retained; logout rejected pending ticket, heartbeat, save and public access; release succeeded; persisted revision remained 1.");
            }
            else throw new InvalidOperationException("Unknown fixture action.");
        }
    }
    return 0;
    Task WriteFixture() => File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(fixture, json));
}
catch (Exception error)
{
    // Exception bodies can contain a credential-bearing Mongo URI or HTTP payload.
    Console.Error.WriteLine("Fixture action failed (" + error.GetType().Name + "). No credentials were logged.");
    return 1;
}

static string Secret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
static string Origin()
{
    var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return "http://127.0.0.1:" + port;
}
static MongoClient Mongo(string uri)
{
    var settings = MongoClientSettings.FromConnectionString(uri); settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
    return new MongoClient(settings);
}
static void Require(HttpResponseMessage result, string operation)
{
    if (!result.IsSuccessStatusCode) throw new InvalidOperationException(operation + " rejected with HTTP " + (int)result.StatusCode);
}
static void Expect(HttpResponseMessage result, HttpStatusCode expected)
{
    if (result.StatusCode != expected) throw new InvalidDataException("A real-service revocation assertion failed.");
}
sealed class Fixture
{
    public string Mongo { get; set; } = "";
    public string Prefix { get; set; } = "";
    public string IdentityBaseUrl { get; set; } = "";
    public string CharactersBaseUrl { get; set; } = "";
    public string RealmsBaseUrl { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string RealmId { get; set; } = "beta-web";
    public string ServerId { get; set; } = "starter-island";
    public string SessionToken { get; set; } = "";
    public string AccountId { get; set; } = "";
    public string DedicatedKey { get; set; } = "";
    public Dictionary<string, Dictionary<string, string>> Services { get; set; } = [];
    public JsonElement[] Characters { get; set; } = [];
}
