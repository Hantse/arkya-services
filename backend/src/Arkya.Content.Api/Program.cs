using System.Text.Json.Serialization;
using Arkya.Content.Api.Application;
using Arkya.Content.Api.Endpoints;
using Arkya.Content.Api.Infrastructure;
using Arkya.Content.Api.Infrastructure.Persistence;
using Arkya.ServiceDefaults;

namespace Arkya.Content.Api;

public partial class Program
{
    public const long MaximumRequestBodyBytes = 512 * 1024;

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = MaximumRequestBodyBytes);
        builder.Services.AddArkyaServiceDefaults(builder.Configuration);
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            options.SerializerOptions.MaxDepth = 16;
        });
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
        builder.Services.AddOptions<ContentAdminOptions>().Bind(builder.Configuration.GetSection("ContentAdmin"))
            .Validate(options => options.Key.Length is >= 32 and <= 512, "ContentAdmin:Key must contain 32 to 512 characters.")
            .Validate(options => !string.Equals(options.Key, builder.Configuration["ServiceAuthentication:Key"], StringComparison.Ordinal),
                "ContentAdmin:Key must differ from ServiceAuthentication:Key.")
            .ValidateOnStart();
        builder.Services.AddSingleton<ContentAdminKeyValidator>();
        builder.Services.AddSingleton<MongoContentRepository>();
        builder.Services.AddSingleton<IContentRepository>(services => services.GetRequiredService<MongoContentRepository>());
        builder.Services.AddHostedService<ContentStoreInitializer>();
        builder.Services.AddScoped<ContentService>();

        var app = builder.Build();
        app.UseArkyaServiceDefaults();
        app.Use(async (context, next) =>
        {
            if (context.Request.ContentLength > MaximumRequestBodyBytes)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return;
            }
            await next(context);
        });
        app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "content" }));
        app.MapContentEndpoints();
        app.Run();
    }
}
