using Arkya.Backend.Api.Application.Auth;
using Arkya.Backend.Api.Application.Servers;
using Arkya.Backend.Api.Endpoints;
using Arkya.Backend.Api.Infrastructure.Auth;
using Arkya.Backend.Api.Infrastructure.Servers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DevAuthOptions>(
    builder.Configuration.GetSection(DevAuthOptions.SectionName));
builder.Services.Configure<MockServerListOptions>(
    builder.Configuration.GetSection(MockServerListOptions.SectionName));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IDevSessionStore, InMemoryDevSessionStore>();
builder.Services.AddSingleton<IGameServerDirectory, MockGameServerDirectory>();
builder.Services.AddScoped<DevAuthService>();

var app = builder.Build();

app.MapHealthEndpoints();
if (app.Environment.IsDevelopment())
{
    app.MapDevAuthEndpoints();
}
app.MapServerEndpoints();

app.Run();
