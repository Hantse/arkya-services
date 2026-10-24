using System.Buffers.Binary;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using Arkya.Reports.Api.Application;
using Arkya.Reports.Api.Infrastructure.Persistence;
using Arkya.ServiceDefaults.Reports;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arkya.Reports.Tests;

internal sealed class FixedClock : TimeProvider
{
    public static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class MemoryReportRepository : IReportRepository
{
    private readonly Dictionary<Guid, StoredReport> reports = [];
    private readonly object gate = new();
    public Task<ReportInsertResult> InsertAsync(StoredReport report, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var prior = reports.Values.SingleOrDefault(item => item.Report.AccountId == report.Report.AccountId &&
                item.Report.ClientReportId == report.Report.ClientReportId);
            if (prior is not null) return Task.FromResult(new ReportInsertResult(prior, false));
            reports.Add(report.Report.Id, report);
            return Task.FromResult(new ReportInsertResult(report, true));
        }
    }

    public Task<BugReportResponse?> FindAsync(Guid id, Guid? accountId, CancellationToken cancellationToken)
    {
        lock (gate) return Task.FromResult(reports.TryGetValue(id, out var stored) &&
            (accountId is null || stored.Report.AccountId == accountId) ? stored.Report : null);
    }

    public Task<BugReportListResponse> ListAsync(Guid? accountId, string? tag, string? status, int offset, int limit,
        CancellationToken cancellationToken, string? kind = null, BugReportListFilters? filters = null)
    {
        lock (gate)
        {
            filters ??= new();
            var matches = reports.Values.Select(item => item.Report).Where(item =>
                (accountId is null || item.AccountId == accountId) && (tag is null || item.Tags.Contains(tag)) &&
                (status is null || item.Status == status) && (kind is null || item.Kind == kind) &&
                (filters.Search is null || item.Title.Contains(filters.Search, StringComparison.OrdinalIgnoreCase) ||
                    item.Description.Contains(filters.Search, StringComparison.OrdinalIgnoreCase) || item.Id.ToString("D").Contains(filters.Search, StringComparison.OrdinalIgnoreCase)) &&
                (filters.Scene is null || item.Scene.Equals(filters.Scene, StringComparison.OrdinalIgnoreCase)) &&
                (filters.ClientVersion is null || item.ClientVersion.Equals(filters.ClientVersion, StringComparison.OrdinalIgnoreCase)) &&
                (filters.Platform is null || item.Platform.Equals(filters.Platform, StringComparison.OrdinalIgnoreCase)) &&
                (filters.CreatedFrom is null || DateOnly.FromDateTime(item.CreatedAt.UtcDateTime) >= filters.CreatedFrom) &&
                (filters.CreatedTo is null || DateOnly.FromDateTime(item.CreatedAt.UtcDateTime) <= filters.CreatedTo)).ToArray();
            var ordered = filters.Sort switch
            {
                BugReportSorts.Oldest => matches.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id.ToString("D"), StringComparer.Ordinal),
                BugReportSorts.Updated => matches.OrderByDescending(item => item.UpdatedAt).ThenByDescending(item => item.Id.ToString("D"), StringComparer.Ordinal),
                _ => matches.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id.ToString("D"), StringComparer.Ordinal)
            };
            return Task.FromResult(new BugReportListResponse(ordered.Skip(offset).Take(limit)
                .Select(item => item with { History = [] }).ToArray(), offset, limit, matches.LongLength));
        }
    }

    public Task<byte[]?> ScreenshotAsync(Guid id, CancellationToken cancellationToken)
    {
        lock (gate) return Task.FromResult(reports.GetValueOrDefault(id)?.Screenshot);
    }

    public Task<BugReportResponse?> UpdateAsync(Guid id, long expectedRevision, string status, string[] tags,
        string resolution, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (!reports.TryGetValue(id, out var stored) || stored.Report.Revision != expectedRevision)
                return Task.FromResult<BugReportResponse?>(null);
            var updated = stored.Report with
            {
                Status = status, Tags = tags, Resolution = resolution, Revision = expectedRevision + 1, UpdatedAt = now,
                History = stored.Report.History.Append(new(expectedRevision + 1, now, actor, status, tags, resolution)).TakeLast(50).ToArray()
            };
            reports[id] = stored with { Report = updated };
            return Task.FromResult<BugReportResponse?>(updated);
        }
    }
}

internal sealed class StubSessions : ISessionValidator
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public Task<SessionValidationResult> ValidateAsync(string sessionToken, CancellationToken cancellationToken) =>
        Task.FromResult(sessionToken switch
        {
            "alice" => new(SessionValidationStatus.Valid, new(Alice, "Alice", FixedClock.Now.AddHours(1))),
            "bob" => new(SessionValidationStatus.Valid, new(Bob, "Bob", FixedClock.Now.AddHours(1))),
            "down" => new(SessionValidationStatus.Unavailable),
            _ => new SessionValidationResult(SessionValidationStatus.Invalid)
        });
}

internal sealed class ReportsFactory(bool production = false) : WebApplicationFactory<Arkya.Reports.Api.Program>
{
    public const string AdminKey = "reports-administrator-test-key-123456789";
    public const string ServiceKey = "reports-service-test-key-123456789123456";
    public Dictionary<string, string?> Settings { get; } = [];
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(production ? "Production" : "Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReportsAdmin:Key"] = AdminKey, ["ServiceAuthentication:Key"] = ServiceKey,
                ["Identity:BaseUrl"] = "https://identity.example/", ["Identity:ServiceKey"] = ServiceKey,
                ["Mongo:ConnectionString"] = "mongodb://localhost:27017", ["Mongo:DatabaseName"] = "arkya_reports_tests",
                ["WebClient:AllowedOrigins:0"] = "https://arkya.example"
            });
            configuration.AddInMemoryCollection(Settings);
        });
        builder.ConfigureServices(services =>
        {
            services.Remove(services.Single(descriptor => descriptor.ImplementationType == typeof(ReportStoreInitializer)));
            services.RemoveAll<IReportRepository>();
            services.AddSingleton<IReportRepository, MemoryReportRepository>();
            services.RemoveAll<ISessionValidator>();
            services.AddSingleton<ISessionValidator, StubSessions>();
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider, FixedClock>();
        });
    }

    public HttpClient Client(string? player = null, bool admin = false, bool https = true)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(https ? "https://localhost" : "http://localhost") });
        if (player is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", player);
        if (admin) client.DefaultRequestHeaders.Add("X-Arkya-Reports-Admin-Key", AdminKey);
        return client;
    }
}

internal static class Fixtures
{
    public static StoredReport StoredReport(Guid id, DateTimeOffset createdAt, DateTimeOffset? updatedAt = null)
    {
        var image = Png();
        return new(new BugReportResponse(1, id, Guid.NewGuid(), StubSessions.Alice, "Collision dans le port",
            "Le personnage traverse le quai.", "test-v1", "StarterIsland", "WindowsPlayer", BugReportStatuses.New,
            [], "", 1, createdAt, updatedAt ?? createdAt,
            new(1, 1, image.Length, "image/png", Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(image))), []), "test", image);
    }

    public static SubmitBugReportRequest Request(Guid? clientId = null) =>
        new(clientId ?? Guid.NewGuid(), "Collision dans le port", "Le personnage traverse le quai.", "test-v1",
            "StarterIsland", "WindowsPlayer", Convert.ToBase64String(Png()));

    public static byte[] Png(int width = 1, int height = 1, byte color = 6, byte filter = 0, bool metadata = false,
        bool end = true, int? rasterLength = null)
    {
        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = 8; header[9] = color;
        Chunk(output, "IHDR", header);
        if (metadata) Chunk(output, "tEXt", Encoding.UTF8.GetBytes("private-path\0C:\\Users\\name"));
        using var compressed = new MemoryStream();
        var raster = new byte[rasterLength ?? Math.Min(16 * 1024 * 1024, ((color == 2 ? 3 : 4) * width + 1) * height)];
        if (raster.Length > 0) raster[0] = filter;
        using (var zlib = new ZLibStream(compressed, CompressionMode.Compress, leaveOpen: true)) zlib.Write(raster);
        Chunk(output, "IDAT", compressed.ToArray());
        if (end) Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void Chunk(Stream output, string name, byte[] payload)
    {
        Span<byte> numeric = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(numeric, payload.Length);
        output.Write(numeric);
        var bytes = Encoding.ASCII.GetBytes(name).Concat(payload).ToArray();
        output.Write(bytes);
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ (0xedb88320u & (uint)-(int)(crc & 1));
        }
        BinaryPrimitives.WriteUInt32BigEndian(numeric, ~crc);
        output.Write(numeric);
    }
}
