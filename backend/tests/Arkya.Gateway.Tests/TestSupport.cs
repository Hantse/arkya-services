using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Arkya.Gateway.Tests;

internal sealed record ReceivedRequest(string Method, string Path, string Query, string Body,
    IReadOnlyDictionary<string, string> Headers);

internal sealed class Downstream : IAsyncDisposable
{
    private readonly WebApplication app;
    public string Address { get; }
    public ConcurrentQueue<ReceivedRequest> Requests { get; } = new();

    private Downstream(WebApplication app, string address)
    {
        this.app = app;
        Address = address;
    }

    public static async Task<Downstream> StartAsync(string name, Func<HttpContext, Task>? respond = null)
    {
        var builder = WebApplication.CreateBuilder();
        // This recorder accepts either public host independently of the gateway's appsettings.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["AllowedHosts"] = "*" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        Downstream? downstream = null;
        app.Run(async context =>
        {
            var body = await new StreamReader(context.Request.Body).ReadToEndAsync(context.RequestAborted);
            downstream!.Requests.Enqueue(new ReceivedRequest(context.Request.Method, context.Request.Path,
                context.Request.QueryString.Value ?? "", body,
                context.Request.Headers.ToDictionary(header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase)));
            if (respond is not null) await respond(context);
            else await context.Response.WriteAsJsonAsync(new { service = name }, context.RequestAborted);
        });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        downstream = new Downstream(app, address + "/");
        return downstream;
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}

internal sealed class GatewayFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> settings;
    private readonly int localPort;
    private readonly string remoteIp;
    public LogCapture Logs { get; } = new();

    public GatewayFactory(string identityAddress, string? charactersAddress = null,
        Dictionary<string, string?>? overrides = null, int localPort = 443, string remoteIp = "127.0.0.1")
    {
        this.localPort = localPort;
        this.remoteIp = remoteIp;
        settings = new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "api.arkya.online",
            ["Gateway:CharactersEnabled"] = (charactersAddress is not null).ToString(),
            ["ReverseProxy:Clusters:identity:Destinations:primary:Address"] = identityAddress,
            ["ReverseProxy:Clusters:characters:Destinations:primary:Address"] = charactersAddress ?? identityAddress,
            ["ProxyTrust:KnownProxies:0"] = "127.0.0.1",
            ["Management:Port"] = "8081"
        };
        if (overrides is not null)
            foreach (var setting in overrides) settings[setting.Key] = setting.Value;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IStartupFilter>(new ConnectionFilter(localPort, remoteIp));
            services.AddSingleton<ILoggerProvider>(Logs);
        });
    }

    public HttpClient Client(string scheme = "https") => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri($"{scheme}://api.arkya.online"), AllowAutoRedirect = false, HandleCookies = false
    });

    private sealed class ConnectionFilter(int port, string address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                context.Connection.LocalPort = port;
                context.Connection.RemoteIpAddress = IPAddress.Parse(address);
                await continuation(context);
            });
            next(app);
        };
    }
}

internal sealed class LogCapture : ILoggerProvider
{
    public ConcurrentQueue<string> Messages { get; } = new();
    public ILogger CreateLogger(string categoryName) => new CaptureLogger(Messages);
    public void Dispose() { }

    private sealed class CaptureLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception)
                + (exception is null ? "" : Environment.NewLine + exception));
    }
}
