using System.Net;
using System.Text.Json;
using Arkya.Admin.Inference;
using Arkya.ServiceDefaults.Inference;
using Microsoft.Extensions.Options;

namespace Arkya.Admin.Tests;

public sealed class InferenceClientTests
{
    [Theory]
    [InlineData("busy", "En conversation", "pending")]
    [InlineData("offline", "Hors ligne", "draft")]
    [InlineData("unhealthy", "Indisponible", "failed")]
    public void WorkerRuntimeStatesHaveFrenchLabelsAndMatchingPresentation(string state, string label, string cssClass)
    {
        Assert.Equal(label, InferencePresentation.StateLabel(state));
        Assert.Equal(cssClass, InferencePresentation.StateClass(state));
    }

    [Fact]
    public async Task ClientUsesOperatorCredentialAndExplicitBodiesForAllOperations()
    {
        using var stub = new InferenceStub();
        using var http = new HttpClient(stub) { BaseAddress = new Uri("https://inference.example/") };
        var client = Client(http);
        Assert.Single(await client.ListAsync(default));
        Assert.Equal(InferenceModels.Catalog, await client.ModelsAsync(default));
        var grant = await client.PrepareInstallationAsync(default);
        Assert.Equal(InferenceStub.Token, grant.Token);
        Assert.Equal(stub.GrantExpiresAt, grant.ExpiresAt);
        Assert.Equal("/admin/inference/enrollment-grants", stub.Calls[^1].Path);
        Assert.Null(stub.Calls[^1].Body);
        Assert.Equal(InferenceStub.AgentId, (await client.GetAsync(InferenceStub.AgentId, default)).AgentId);
        var created = await client.CreateAsync(new(InferenceStub.AgentId, "Ma machine", 0, 4, 6), default);
        Assert.Equal(InferenceStub.Token, created.AgentToken);
        var create = stub.Calls.Single(call => call.Method == HttpMethod.Post && call.Path == "/admin/inference/agents").Body!.Value;
        Assert.Equal(6, create.EnumerateObject().Count());
        Assert.Equal(new CreateInferenceAgentRequest(InferenceStub.AgentId, "Ma machine", 0, 4, 6),
            create.Deserialize<CreateInferenceAgentRequest>(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await client.AddWorkerAsync(InferenceStub.AgentId, 5, default, "qwen35-08b");
        var add = stub.Calls[^1];
        Assert.Equal("/admin/inference/agents/cpu-bureau/workers", add.Path);
        Assert.Equal(2, add.Body!.Value.EnumerateObject().Count());
        Assert.Equal(5, add.Body.Value.GetProperty("threads").GetInt32());
        Assert.Equal("qwen35-08b", add.Body.Value.GetProperty("modelId").GetString());
        await client.UpdateWorkerAsync(InferenceStub.AgentId, "worker-added", 8, "lfm", default);
        var update = stub.Calls[^1];
        Assert.Equal(HttpMethod.Patch, update.Method);
        Assert.Equal("/admin/inference/agents/cpu-bureau/workers/worker-added", update.Path);
        Assert.Equal(2, update.Body!.Value.EnumerateObject().Count());
        Assert.Equal(new UpdateInferenceWorkerRequest(8, "lfm"),
            update.Body.Value.Deserialize<UpdateInferenceWorkerRequest>(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await client.DeleteWorkerAsync(InferenceStub.AgentId, "worker-added", default);
        Assert.Null(stub.Calls[^1].Body);
        await client.DeleteAgentAsync(InferenceStub.AgentId, default);
        Assert.Equal(HttpMethod.Delete, stub.Calls[^1].Method);
        Assert.Equal("/admin/inference/agents/cpu-bureau", stub.Calls[^1].Path);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task UpstreamBodiesAreNotIncludedInErrors(HttpStatusCode status)
    {
        using var stub = new InferenceStub { ReadError = status };
        using var http = new HttpClient(stub) { BaseAddress = new Uri("https://inference.example/") };
        var error = await Assert.ThrowsAsync<InferenceApiException>(() => Client(http).ListAsync(default));
        Assert.Equal(status, error.StatusCode);
        Assert.DoesNotContain(InferenceFactory.AdminKey, error.Message);
        Assert.DoesNotContain(InferenceStub.Token, error.Message);
        Assert.DoesNotContain("private upstream details", error.Message);
    }

    [Fact]
    public async Task InvalidJsonBecomesASanitizedDependencyError()
    {
        using var stub = new InferenceStub { InvalidJson = true };
        using var http = new HttpClient(stub) { BaseAddress = new Uri("https://inference.example/") };
        var error = await Assert.ThrowsAsync<InferenceApiException>(() => Client(http).ListAsync(default));
        Assert.Equal(HttpStatusCode.BadGateway, error.StatusCode);
        Assert.DoesNotContain(InferenceFactory.AdminKey, error.Message);
    }

    [Fact]
    public async Task DisabledClientNeverContactsTheService()
    {
        using var stub = new InferenceStub();
        using var http = new HttpClient(stub);
        var client = new InferenceAdminClient(http, Options.Create(new InferenceApiOptions()));
        await Assert.ThrowsAsync<InferenceApiException>(() => client.ListAsync(default));
        await Assert.ThrowsAsync<InferenceApiException>(() => client.AddWorkerAsync("cpu-bureau", 4, default));
        await Assert.ThrowsAsync<InferenceApiException>(() => client.PrepareInstallationAsync(default));
        await Assert.ThrowsAsync<InferenceApiException>(() => client.ModelsAsync(default));
        await Assert.ThrowsAsync<InferenceApiException>(() => client.UpdateWorkerAsync("cpu-bureau", "worker-one", 4, "lfm", default));
        Assert.Empty(stub.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task EnrollmentAndUpdateErrorsNeverExposeUpstreamCredentials(HttpStatusCode status)
    {
        using var stub = new InferenceStub { WriteError = status };
        using var http = new HttpClient(stub) { BaseAddress = new Uri("https://inference.example/") };
        var client = Client(http);
        var grantError = await Assert.ThrowsAsync<InferenceApiException>(() => client.PrepareInstallationAsync(default));
        var patchError = await Assert.ThrowsAsync<InferenceApiException>(() => client.UpdateWorkerAsync(InferenceStub.AgentId, "worker-one", 4, "lfm", default));
        foreach (var error in new[] { grantError, patchError })
        {
            Assert.Equal(status, error.StatusCode);
            Assert.DoesNotContain(InferenceFactory.AdminKey, error.Message);
            Assert.DoesNotContain(InferenceStub.Token, error.Message);
            Assert.DoesNotContain("private upstream details", error.Message);
        }
    }

    private static InferenceAdminClient Client(HttpClient http) => new(http, Options.Create(new InferenceApiOptions
    {
        Enabled = true, AdminKey = InferenceFactory.AdminKey
    }));
}
