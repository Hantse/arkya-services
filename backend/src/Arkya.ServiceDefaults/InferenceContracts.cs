namespace Arkya.ServiceDefaults.Inference;

public static class InferenceModels
{
    public const string Lfm = "hf.co/LiquidAI/LFM2.5-1.2B-Instruct-GGUF:Q4_K_M";
}

public sealed record CreateInferenceAgentRequest(string AgentId, string DisplayName,
    int InitialWorkers = 3, int ThreadsPerWorker = 4, int MaxWorkers = 6);

// Returned only at creation. Operators must keep this credential out of logs and URLs.
public sealed record CreateInferenceAgentResponse(InferenceAgentSummary Agent, string AgentToken);

public sealed record AddInferenceWorkerRequest(int Threads = 4);

public sealed record InferenceWorkerDesired(string WorkerId, int Threads);

public sealed record InferenceWorkerObserved(string WorkerId, string State, int? Pid, int Port,
    int Threads, string? LastError);

public sealed record InferenceAgentSummary(int SchemaVersion, string AgentId, string DisplayName,
    long Revision, int MaxWorkers, int ThreadsPerWorker, DateTimeOffset CreatedAt,
    DateTimeOffset? LastSeenAt, bool Online, string? MachineName, string? Version,
    int? LogicalProcessors, long? TotalMemoryMb, int? DemoPort, string? LastError,
    InferenceWorkerDesired[] DesiredWorkers, InferenceWorkerObserved[] ObservedWorkers);

public sealed record InferenceHeartbeatRequest(string AgentId, string MachineName, string Version,
    int LogicalProcessors, long TotalMemoryMb, InferenceWorkerObserved[] Workers,
    int DemoPort, string? LastError, long AppliedRevision = 0);

public sealed record InferenceHeartbeatResponse(string AgentId, long Revision,
    InferenceWorkerDesired[] Workers, int HeartbeatSeconds = 5);
