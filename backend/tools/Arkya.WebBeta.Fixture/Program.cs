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
if (args.Length < 2) { Console.Error.WriteLine("Use prepare|characters|tickets|seed-partition-state|client-config|partition-state|verify-partition-state|verify-partition-relogin|verify-state|verify-revocation|cleanup <private-directory>."); return 2; }
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
        var partitioned = Environment.GetEnvironmentVariable("ARKYA_TEST_SERVER_COUNT") == "4";
        fixture.MultiArea = Environment.GetEnvironmentVariable("ARKYA_TEST_MULTI_AREA") == "1";
        if (fixture.MultiArea && !partitioned) throw new InvalidOperationException("Multi-Area validation requires four dedicateds.");
        if (partitioned)
        {
            var endpoint = new Uri(fixture.Endpoint);
            if (!endpoint.IsLoopback || endpoint.Scheme != "ws" || endpoint.Port > 65532)
                throw new InvalidOperationException("Partition validation requires four loopback WebSocket ports.");
            fixture.ServerId = "island-starter-island-01";
            fixture.TopologyVersion = fixture.MultiArea ? "multi-area-2x2-v1" : "starter-island-4-v1";
            fixture.ProtocolVersion = fixture.MultiArea ? 3 : 2;
            fixture.BuildId = fixture.MultiArea ? "arkya-multi-area-v1" : "arkya-partition-handoff-v1";
            fixture.Servers = Enumerable.Range(0, 4).Select(index => new FixtureServer
            {
                ServerId = fixture.MultiArea ? $"island-{(index < 2 ? "starter-island" : "handoff-valley")}-{index % 2 + 1:D2}" : $"island-starter-island-{index + 1:D2}",
                AreaId = fixture.MultiArea && index >= 2 ? "handoff-valley" : "starter-island",
                ContentId = fixture.MultiArea && index >= 2 ? "handoff-valley" : "starter-island",
                Endpoint = new UriBuilder(endpoint) { Port = endpoint.Port + index }.Uri.AbsoluteUri,
                DedicatedKey = index == 0 ? fixture.DedicatedKey : Secret(), PartitionIndex = fixture.MultiArea ? index % 2 : index,
                MinX = index % 2 * 768, MaxX = (index % 2 + 1) * 768,
                MinZ = fixture.MultiArea ? 0 : index / 2 * 768, MaxZ = fixture.MultiArea ? 1536 : (index / 2 + 1) * 768
            }).ToArray();
        }
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
        realms["GameAccess__StandaloneEntries__0__ProtocolVersion"] = fixture.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
        realms["GameAccess__StandaloneEntries__0__BuildId"] = fixture.BuildId;
        var characters = Common("_characters", fixture.CharactersBaseUrl);
        characters["ServiceAuthentication__Key"] = Secret();
        characters["Identity__BaseUrl"] = fixture.IdentityBaseUrl; characters["Identity__ServiceKey"] = identityKey;
        characters["Realms__BaseUrl"] = fixture.RealmsBaseUrl; characters["Realms__ServiceKey"] = realmKey;
        characters["GameAccess__DedicatedServers__0__RealmId"] = fixture.RealmId;
        characters["GameAccess__DedicatedServers__0__ServerId"] = fixture.ServerId;
        characters["GameAccess__DedicatedServers__0__Key"] = fixture.DedicatedKey;
        if (partitioned)
        {
            realms["GameAccess__StandaloneEntries__0__TopologyVersion"] = fixture.TopologyVersion!;
            for (var slot = 0; slot < fixture.Servers.Length; slot++)
            {
                var server = fixture.Servers[slot];
                var prefix = $"GameAccess__StandaloneEntries__0__Partitions__{slot}__";
                realms[prefix + "ServerId"] = server.ServerId;
                realms[prefix + "Endpoint"] = server.Endpoint;
                realms[prefix + "IslandId"] = server.AreaId;
                realms[prefix + "PartitionIndex"] = server.PartitionIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
                realms[prefix + "Bounds__MinX"] = server.MinX.ToString(System.Globalization.CultureInfo.InvariantCulture);
                realms[prefix + "Bounds__MaxX"] = server.MaxX.ToString(System.Globalization.CultureInfo.InvariantCulture);
                realms[prefix + "Bounds__MinZ"] = server.MinZ.ToString(System.Globalization.CultureInfo.InvariantCulture);
                realms[prefix + "Bounds__MaxZ"] = server.MaxZ.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var identityPrefix = $"GameAccess__DedicatedServers__{slot}__";
                characters[identityPrefix + "RealmId"] = fixture.RealmId;
                characters[identityPrefix + "ServerId"] = server.ServerId;
                characters[identityPrefix + "Key"] = server.DedicatedKey;
            }
            if (fixture.MultiArea)
            {
                var areas = new[] { "starter-island", "handoff-valley" };
                for (var index = 0; index < areas.Length; index++)
                {
                    var prefix = $"GameAccess__StandaloneEntries__0__Areas__{index}__";
                    realms[prefix + "AreaId"] = areas[index]; realms[prefix + "ContentId"] = areas[index];
                    var transition = $"GameAccess__StandaloneEntries__0__AreaTransitions__{index}__";
                    realms[transition + "Id"] = index == 0 ? "starter-to-valley" : "valley-to-starter";
                    realms[transition + "SourceAreaId"] = areas[index]; realms[transition + "TargetAreaId"] = areas[1 - index];
                    realms[transition + "SourceBounds__MinX"] = "810"; realms[transition + "SourceBounds__MaxX"] = "820";
                    realms[transition + "SourceBounds__MinZ"] = "740"; realms[transition + "SourceBounds__MaxZ"] = "760";
                    realms[transition + "TargetPosition__X"] = index == 0 ? "750" : "790";
                    realms[transition + "TargetPosition__Y"] = index == 0 ? "20" : "38";
                    realms[transition + "TargetPosition__Z"] = "750";
                    realms[transition + "TargetForward__X"] = "1"; realms[transition + "TargetForward__Z"] = "0";
                }
            }
            await File.WriteAllTextAsync(Path.Combine(directory, "partitions.json"), JsonSerializer.Serialize(new
            {
                realmId = fixture.RealmId, topologyVersion = fixture.TopologyVersion,
                protocolVersion = fixture.ProtocolVersion, buildId = fixture.BuildId,
                partitions = fixture.Servers.Select(server => new { server.ServerId, server.Endpoint, islandId = server.AreaId,
                    server.ContentId, server.PartitionIndex, bounds = new { server.MinX, server.MinZ, server.MaxX, server.MaxZ } })
            }, json));
        }
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
        if (action is "partition-state" or "verify-partition-state")
        {
            if (fixture.Servers.Length != 4 || fixture.Characters.Length == 0)
                throw new InvalidDataException("Partition verification requires the four-server fixture.");
            var collection = Mongo(fixture.Mongo).GetDatabase(fixture.Prefix + "_characters").GetCollection<BsonDocument>("game_leases");
            var id = fixture.Characters[0].GetProperty("id").GetString();
            var record = await collection.Find(new BsonDocument("_id", id)).SingleOrDefaultAsync()
                ?? throw new InvalidDataException("The fixture character has no lease record.");
            var state = record.GetValue("State", BsonNull.Value);
            var transfer = record.GetValue("Transfer", BsonNull.Value);
            var position = state.IsBsonDocument ? state["Position"].AsBsonDocument : null;
            var active = Guid.Parse(record["LeaseId"].AsString) != Guid.Empty && record["LeaseExpiresAt"].ToUniversalTime() > DateTime.UtcNow;
            var epoch = record["AuthorityEpoch"].ToInt64();
            var serverId = record["ServerId"].AsString;
            var revision = state.IsBsonDocument ? state["Revision"].ToInt64() : 0;
            var areaId = state.IsBsonDocument ? state.AsBsonDocument.GetValue("IslandId", BsonNull.Value).IsString ? state["IslandId"].AsString : null : null;
            object? simulationSummary = null;
            if (state.IsBsonDocument && state.AsBsonDocument.GetValue("Simulation", BsonNull.Value) is BsonDocument simulationState)
            {
                using var captured = JsonDocument.Parse(simulationState["Payload"].AsString);
                var value = captured.RootElement;
                simulationSummary = new { version = value.GetProperty("version").GetInt32(), lastSequence = value.GetProperty("lastSequence").GetUInt32(),
                    jumpSequence = value.GetProperty("jumpSequence").GetUInt32(), consumedJumpSequence = value.GetProperty("consumedJumpSequence").GetUInt32(),
                    phase = value.GetProperty("phase").GetInt32(), verticalVelocity = value.GetProperty("verticalVelocity").GetDouble(),
                    gameplay = value.TryGetProperty("gameplay", out var gameplay) ? gameplay.Clone() : (JsonElement?)null };
            }
            var expectedFinal = fixture.MultiArea ? fixture.Servers[3] : fixture.Servers[0];
            var expectedX = fixture.MultiArea ? 790 : 750;
            if (action == "verify-partition-state" && (position is null || revision < 2 || epoch < 6 || active ||
                serverId != expectedFinal.ServerId || fixture.MultiArea && areaId != expectedFinal.AreaId ||
                Math.Abs(position["X"].ToDouble() - expectedX) > 5 || Math.Abs(position["Z"].ToDouble() - 750) > 5))
                throw new InvalidDataException("The four-server traversal did not persist and release its final authority/state.");
            if (action == "verify-partition-state")
            {
                var simulation = state.AsBsonDocument.GetValue("Simulation", BsonNull.Value);
                if (!simulation.IsBsonDocument || simulation["SchemaVersion"].AsInt32 != 1)
                    throw new InvalidDataException("The authoritative traversal state was not persisted.");
                using var captured = JsonDocument.Parse(simulation["Payload"].AsString);
                if (captured.RootElement.GetProperty("lastSequence").GetUInt32() == 0 ||
                    captured.RootElement.GetProperty("jumpSequence").GetUInt32() == 0 ||
                    captured.RootElement.GetProperty("consumedJumpSequence").GetUInt32() == 0)
                    throw new InvalidDataException("The final input and jump history was lost.");
                if (fixture.MultiArea && (captured.RootElement.GetProperty("version").GetInt32() != 2 ||
                    !captured.RootElement.TryGetProperty("gameplay", out var gameplay) || gameplay.GetProperty("version").GetInt32() != 1))
                    throw new InvalidDataException("The complete gameplay snapshot was not persisted.");
                if (fixture.MultiArea)
                    foreach (var expected in SeededGameplay().EnumerateObject())
                        if (captured.RootElement.GetProperty("gameplay").GetProperty("value").GetProperty(expected.Name).GetInt32() != expected.Value.GetInt32())
                            throw new InvalidDataException("The persisted nondefault gameplay state changed during traversal.");
            }
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                observedAt = DateTimeOffset.UtcNow, serverId, areaId, authorityEpoch = epoch, revision, activeLease = active,
                simulation = simulationSummary,
                transfer = transfer.IsBsonDocument ? new
                {
                    status = transfer["Status"].AsString, sourceServerId = transfer["SourceServerId"].AsString,
                    sourceEpoch = transfer["SourceEpoch"].ToInt64(), targetServerId = transfer["TargetServerId"].AsString
                } : null,
                position = position is null ? null : new { x = position["X"].ToDouble(), y = position["Y"].ToDouble(), z = position["Z"].ToDouble() },
                verified = action == "verify-partition-state"
            }, json));
        }
        else if (action is "client-bootstrap" or "client-bootstrap-resume")
        {
            var resume = action == "client-bootstrap-resume";
            await File.WriteAllTextAsync(Path.Combine(directory, resume ? "client-resume.json" : "client.json"), JsonSerializer.Serialize(new
            {
                awaitAdmission = true, configurationNonce = Guid.NewGuid().ToString("N"), character = fixture.Characters[0],
                reportPath = Path.Combine(directory, resume ? "client-resume-report.json" : "client-report.json"), timeoutSeconds = 240
            }, json));
            Console.WriteLine("Credential-free client bootstrap written.");
        }
        else if (action is "client-config" or "client-resume-config")
        {
            if (fixture.Servers.Length != 4) throw new InvalidDataException("Client configuration requires four actual dedicateds.");
            var resume = action == "client-resume-config";
            var configurationPath = Path.Combine(directory, resume ? "client-resume.json" : "client.json");
            using var bootstrap = JsonDocument.Parse(await File.ReadAllTextAsync(configurationPath));
            var nonce = bootstrap.RootElement.GetProperty("configurationNonce").GetString();
            using var tickets = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "tickets.json")));
            var primary = tickets.RootElement.GetProperty("clients")[0];
            BsonDocument? savedState = null;
            if (resume)
            {
                var id = fixture.Characters[0].GetProperty("id").GetString();
                var record = await Mongo(fixture.Mongo).GetDatabase(fixture.Prefix + "_characters").GetCollection<BsonDocument>("game_leases")
                    .Find(new BsonDocument("_id", id)).SingleAsync();
                savedState = record["State"].AsBsonDocument;
                if (savedState["IslandId"].AsString != "handoff-valley") throw new InvalidDataException("Resume must use the persisted Valley Area.");
            }
            var resumeServer = fixture.Servers.Single(server => server.ServerId == primary.GetProperty("admission").GetProperty("serverId").GetString());
            var configurationJson = JsonSerializer.Serialize(new
            {
                configurationNonce = nonce, resumeOnly = resume,
                character = primary.GetProperty("character"), admission = primary.GetProperty("admission"),
                reportPath = Path.Combine(directory, resume ? "client-resume-report.json" : "client-report.json"), timeoutSeconds = 240,
                expectedResumePosition = savedState is null ? null : new { x = savedState["Position"]["X"].ToDouble(), y = savedState["Position"]["Y"].ToDouble(), z = savedState["Position"]["Z"].ToDouble() },
                expectedResumeForward = savedState is null ? null : new { x = savedState["Forward"]["X"].ToDouble(), z = savedState["Forward"]["Z"].ToDouble() },
                waypoints = resume ? new[] { new FixtureWaypoint { X = (float)savedState!["Position"]["X"].ToDouble(), Z = (float)savedState["Position"]["Z"].ToDouble(),
                    ServerId = resumeServer.ServerId, AreaId = resumeServer.AreaId, ContentId = resumeServer.ContentId } } : fixture.MultiArea ? new[]
                {
                    Point(750, 750, 0), Point(790, 750, 1), Portal(2, "starter-island", "starter-to-valley", 750),
                    Point(790, 750, 3), Portal(1, "handoff-valley", "valley-to-starter", 790), Point(750, 750, 0),
                    Point(790, 750, 1), Portal(2, "starter-island", "starter-to-valley", 750), Point(790, 750, 3)
                } : new[]
                {
                    Point(750, 750, 0), Point(786, 750, 1), Point(786, 786, 3), Point(750, 786, 2),
                    Point(750, 750, 0), Point(786, 786, 3), Point(750, 750, 0)
                },
                multiArea = fixture.MultiArea, expectedGameplay = fixture.MultiArea ? SeededGameplay() : (JsonElement?)null,
                expectedHandoffs = resume ? 0 : fixture.MultiArea ? 8 : 6,
                expectedAreaTransitions = resume ? 0 : fixture.MultiArea ? 3 : 0
            }, json);
            var temporary = configurationPath + ".tmp";
            await File.WriteAllTextAsync(temporary, configurationJson);
            for (var attempt = 0; ; attempt++)
            {
                try { File.Move(temporary, configurationPath, true); break; }
                catch (IOException) when (attempt < 20) { await Task.Delay(25); }
            }
            Console.WriteLine("Client configuration written; credentials were not logged.");
            FixtureWaypoint Point(float x, float z, int slot) => new() { X = x, Z = z, ServerId = fixture.Servers[slot].ServerId,
                AreaId = fixture.Servers[slot].AreaId, ContentId = fixture.Servers[slot].ContentId };
            FixtureWaypoint Portal(int slot, string sourceArea, string transition, float arrivalX) => new()
            { X = arrivalX, Z = 750, ServerId = fixture.Servers[slot].ServerId, AreaId = fixture.Servers[slot].AreaId,
                ContentId = fixture.Servers[slot].ContentId, SourceAreaId = sourceArea, TransitionId = transition, ApproachX = 815, ApproachZ = 750 };
        }
        else if (action == "verify-state")
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
            foreach (var file in new[] { "fixture.json", "tickets.json", "client.json", "client.json.tmp", "client-resume.json", "client-resume.json.tmp", "partitions.json" })
                if (File.Exists(Path.Combine(directory, file))) File.Delete(Path.Combine(directory, file));
            Console.WriteLine("Isolated test sessions and databases removed.");
        }
        else
        {
            using var http = new HttpClient { BaseAddress = new Uri(fixture.CharactersBaseUrl), Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.SessionToken);
            if (action == "seed-partition-state")
            {
                if (fixture.Servers.Length != 4 || fixture.Characters.Length == 0)
                    throw new InvalidDataException("State seeding requires the four-server fixture.");
                var id = fixture.Characters[0].GetProperty("id").GetString();
                using var issued = await http.PostAsJsonAsync($"/v1/characters/{id}/admissions", new
                { realmId = fixture.RealmId, protocolVersion = fixture.ProtocolVersion, buildId = fixture.BuildId });
                Require(issued, "Initial seed admission");
                var admission = await issued.Content.ReadFromJsonAsync<JsonElement>();
                var seedServer = fixture.Servers.Single(server => server.ServerId == admission.GetProperty("serverId").GetString());
                if (fixture.MultiArea && (seedServer.AreaId != "starter-island" || admission.GetProperty("areaId").GetString() != "starter-island"))
                    throw new InvalidDataException("Initial admission must select the trusted Starter Area.");
                using var dedicated = new HttpClient { BaseAddress = new Uri(fixture.CharactersBaseUrl), Timeout = TimeSpan.FromSeconds(15) };
                dedicated.DefaultRequestHeaders.Add("X-Arkya-Dedicated-Key", seedServer.DedicatedKey);
                using var consumed = await dedicated.PostAsJsonAsync("/internal/game/admissions/consume", new { ticket = admission.GetProperty("ticket").GetString() });
                Require(consumed, "Initial seed lease");
                var lease = await consumed.Content.ReadFromJsonAsync<JsonElement>();
                var leaseBody = new { leaseId = lease.GetProperty("leaseId").GetString(), authorityEpoch = lease.GetProperty("authorityEpoch").GetInt64() };
                var route = $"/internal/game/leases/{id}";
                using var saved = await dedicated.PutAsJsonAsync(route + "/state", new
                {
                    leaseBody.leaseId, leaseBody.authorityEpoch, expectedRevision = 0,
                    position = new { x = 750, y = fixture.MultiArea ? 41.2525 : 100, z = 750 }, forward = new { x = 1, z = 0 },
                    simulation = fixture.MultiArea ? new
                    {
                        schemaVersion = 1,
                        payload = JsonSerializer.Serialize(new { version = 2, motion = 0, run = 0, inputX = 0, inputZ = 0,
                            verticalVelocity = 0, colliderSupportHeight = 0, inputAgeSeconds = 0, lastSequence = 17,
                            jumpSequence = 4, consumedJumpSequence = 4, phase = 0, colliderSupported = 0,
                            gameplay = new { version = 1, value = SeededGameplay() } })
                    } : null
                });
                Require(saved, "Initial center position");
                using var release = await dedicated.PostAsJsonAsync(route + "/release", leaseBody); Require(release, "Initial seed release");
                Console.WriteLine("Seeded the starting position through a real lease/save/release; normal input drives all later movement.");
            }
            else if (action == "characters")
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
                    { realmId = fixture.RealmId, protocolVersion = fixture.ProtocolVersion, buildId = fixture.BuildId });
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
            else if (action == "verify-partition-relogin")
            {
                if (fixture.Servers.Length != 4) throw new InvalidDataException("Relogin verification requires four dedicateds.");
                var id = fixture.Characters[0].GetProperty("id").GetString();
                var collection = Mongo(fixture.Mongo).GetDatabase(fixture.Prefix + "_characters").GetCollection<BsonDocument>("game_leases");
                var before = await collection.Find(new BsonDocument("_id", id)).SingleAsync();
                using var issued = await http.PostAsJsonAsync($"/v1/characters/{id}/admissions", new
                    { realmId = fixture.RealmId, protocolVersion = fixture.ProtocolVersion, buildId = fixture.BuildId });
                Require(issued, "Relogin admission");
                var admission = await issued.Content.ReadFromJsonAsync<JsonElement>();
                if (admission.GetProperty("serverId").GetString() != before["ServerId"].AsString)
                    throw new InvalidDataException("Relogin selected a different authority from the saved destination.");
                using var dedicated = new HttpClient { BaseAddress = new Uri(fixture.CharactersBaseUrl), Timeout = TimeSpan.FromSeconds(15) };
                var selected = fixture.Servers.Single(server => server.ServerId == admission.GetProperty("serverId").GetString());
                dedicated.DefaultRequestHeaders.Add("X-Arkya-Dedicated-Key", selected.DedicatedKey);
                using var consumed = await dedicated.PostAsJsonAsync("/internal/game/admissions/consume", new { ticket = admission.GetProperty("ticket").GetString() });
                Require(consumed, "Relogin lease");
                var lease = await consumed.Content.ReadFromJsonAsync<JsonElement>();
                var state = lease.GetProperty("state");
                var oldState = before["State"].AsBsonDocument;
                if (fixture.MultiArea && (admission.GetProperty("areaId").GetString() != selected.AreaId ||
                    admission.GetProperty("contentId").GetString() != selected.ContentId ||
                    lease.GetProperty("areaId").GetString() != selected.AreaId || lease.GetProperty("contentId").GetString() != selected.ContentId ||
                    state.GetProperty("islandId").GetString() != selected.AreaId || oldState["IslandId"].AsString != selected.AreaId))
                    throw new InvalidDataException("Relogin did not route to the saved Area and content.");
                if (lease.GetProperty("authorityEpoch").GetInt64() != before["AuthorityEpoch"].ToInt64() + 1 ||
                    state.GetProperty("revision").GetInt64() != oldState["Revision"].ToInt64() ||
                    state.GetProperty("simulation").GetProperty("payload").GetString() != oldState["Simulation"]["Payload"].AsString)
                    throw new InvalidDataException("Relogin changed the persisted traversal state.");
                foreach (var coordinate in new[] { "x", "y", "z" })
                    if (Math.Abs(state.GetProperty("position").GetProperty(coordinate).GetDouble() - oldState["Position"][coordinate.ToUpperInvariant()].ToDouble()) > .0001)
                        throw new InvalidDataException("Relogin changed the persisted position.");
                foreach (var coordinate in new[] { "x", "z" })
                    if (Math.Abs(state.GetProperty("forward").GetProperty(coordinate).GetDouble() - oldState["Forward"][coordinate.ToUpperInvariant()].ToDouble()) > .0001)
                        throw new InvalidDataException("Relogin changed the persisted orientation.");
                var leaseBody = new { leaseId = lease.GetProperty("leaseId").GetString(), authorityEpoch = lease.GetProperty("authorityEpoch").GetInt64() };
                using var released = await dedicated.PostAsJsonAsync($"/internal/game/leases/{id}/release", leaseBody); Require(released, "Relogin release");
                var after = await collection.Find(new BsonDocument("_id", id)).SingleAsync();
                if (!after["State"].Equals(before["State"]) || Guid.Parse(after["LeaseId"].AsString) != Guid.Empty)
                    throw new InvalidDataException("Relogin release changed the saved state or retained authority.");
                Console.WriteLine(JsonSerializer.Serialize(new { verified = true, persistedPositionPreserved = true,
                    persistedOrientationPreserved = true, completeSimulationPreserved = true, stateRevision = state.GetProperty("revision").GetInt64(),
                    authorityEpoch = leaseBody.authorityEpoch, areaId = selected.AreaId, contentId = selected.ContentId,
                    serverId = selected.ServerId, persistedAreaPreserved = fixture.MultiArea, released = true }, json));
            }
            else if (action == "verify-revocation")
            {
                if (fixture.Characters.Length != 2) throw new InvalidDataException("Revocation test requires two fixture characters.");
                var admissions = new List<JsonElement>();
                foreach (var character in fixture.Characters)
                {
                    using var issued = await http.PostAsJsonAsync($"/v1/characters/{character.GetProperty("id").GetString()}/admissions",
                        new { realmId = fixture.RealmId, protocolVersion = fixture.ProtocolVersion, buildId = fixture.BuildId });
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
static JsonElement SeededGameplay() => JsonSerializer.SerializeToElement(new Dictionary<string, int>
{
    ["Health"] = 73, ["MaxHealth"] = 127, ["Mana"] = 41, ["MaxMana"] = 95, ["Stamina"] = 63, ["MaxStamina"] = 111,
    ["ActiveClass"] = 1, ["WarriorLevel"] = 3, ["WarriorExperience"] = 41, ["ScoutLevel"] = 2, ["ScoutExperience"] = 17,
    ["MageLevel"] = 0, ["MageExperience"] = 0, ["ClericLevel"] = 0, ["ClericExperience"] = 0,
    ["DruidLevel"] = 0, ["DruidExperience"] = 0, ["LearnedRankOne"] = 1, ["LearnedRankTwo"] = 0
});
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
    public bool MultiArea { get; set; }
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
    public int ProtocolVersion { get; set; } = 1;
    public string BuildId { get; set; } = "arkya-web-beta-v1";
    public string? TopologyVersion { get; set; }
    public FixtureServer[] Servers { get; set; } = [];
    public Dictionary<string, Dictionary<string, string>> Services { get; set; } = [];
    public JsonElement[] Characters { get; set; } = [];
}
sealed class FixtureServer
{
    public string AreaId { get; set; } = "starter-island";
    public string ContentId { get; set; } = "starter-island";
    public string ServerId { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string DedicatedKey { get; set; } = "";
    public int PartitionIndex { get; set; }
    public double MinX { get; set; }
    public double MinZ { get; set; }
    public double MaxX { get; set; }
    public double MaxZ { get; set; }
}
sealed class FixtureWaypoint
{
    public float X { get; set; }
    public float Z { get; set; }
    public string ServerId { get; set; } = "";
    public string AreaId { get; set; } = "";
    public string ContentId { get; set; } = "";
    public string? SourceAreaId { get; set; }
    public string? TransitionId { get; set; }
    public float ApproachX { get; set; }
    public float ApproachZ { get; set; }
}
