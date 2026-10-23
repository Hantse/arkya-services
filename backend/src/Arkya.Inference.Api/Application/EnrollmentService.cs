using Arkya.Inference.Api.Domain;
using Arkya.Inference.Api.Infrastructure;
using Arkya.ServiceDefaults.Inference;

namespace Arkya.Inference.Api.Application;

public sealed class EnrollmentService(IEnrollmentGrantRepository grants, IInferenceRepository agents,
    InferenceService inference, TimeProvider clock)
{
    public async Task<InferenceEnrollmentGrantResponse> CreateGrantAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var token = AgentCredentials.Create();
            var now = clock.GetUtcNow();
            var grant = new EnrollmentGrantRecord(AgentCredentials.Hash(token), now, now.AddMinutes(30));
            if (await grants.TryCreateAsync(grant, cancellationToken)) return new(token, grant.ExpiresAt);
        }
        throw new InvalidOperationException("Could not allocate an enrollment credential.");
    }

    public async Task<InferenceResult<InferenceAgentSummary>> RegisterAsync(RegisterInferenceAgentRequest request,
        string? grantToken, CancellationToken cancellationToken)
    {
        var create = new CreateInferenceAgentRequest(request.AgentId, request.DisplayName, request.InitialWorkers,
            request.ThreadsPerWorker, request.MaxWorkers, request.ModelId);
        if (!inference.ValidCreation(create) || !AgentCredentials.IsValidToken(request.AgentToken))
            return new(null, "invalid_agent");

        var existing = await agents.FindAsync(request.AgentId, cancellationToken);
        if (existing is not null) return Existing(existing, request.AgentToken);
        if (!AgentCredentials.IsValidToken(grantToken)) return new(null, "unauthorized_enrollment");
        var grantHash = AgentCredentials.Hash(grantToken!);
        var deviceHash = AgentCredentials.Hash(request.AgentToken);
        create = create with { DisplayName = create.DisplayName.Replace(grantToken!, "[redacted]", StringComparison.Ordinal) };
        // Binding precedes insertion so competing machines cannot consume the same grant.
        if (!await grants.TryBindAsync(grantHash, request.AgentId, deviceHash, clock.GetUtcNow(), cancellationToken))
            return new(null, "unauthorized_enrollment");

        var result = await inference.CreateWithTokenAsync(create, request.AgentToken, cancellationToken);
        if (result.Error != "agent_exists") return result;
        existing = await agents.FindAsync(request.AgentId, cancellationToken);
        return existing is null ? new(null, "agent_identity_unavailable") : Existing(existing, request.AgentToken);
    }

    private InferenceResult<InferenceAgentSummary> Existing(InferenceAgentRecord agent, string token) =>
        AgentCredentials.Matches(token, agent.TokenHash)
            ? new(inference.Summary(agent), null) : new(null, "agent_exists");
}
