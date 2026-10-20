namespace Arkya.ServiceDefaults.Inference;

public sealed record CreateInferenceAgentRequest(string AgentId, string DisplayName,
    int InitialWorkers = 3, int ThreadsPerWorker = 4, int MaxWorkers = 6,
    string ModelId = InferenceModels.DefaultModelId);

// Returned only at creation. Operators must keep this credential out of logs and URLs.
public sealed record CreateInferenceAgentResponse(InferenceAgentSummary Agent, string AgentToken);

public sealed record InferenceEnrollmentGrantResponse(string Token, DateTimeOffset ExpiresAt);

public sealed record RegisterInferenceAgentRequest(string AgentId, string DisplayName, string AgentToken,
    int InitialWorkers = 3, int ThreadsPerWorker = 4, int MaxWorkers = 6,
    string ModelId = InferenceModels.DefaultModelId);

public sealed record InferenceModelOption(string ModelId, string Label, string OllamaModel);

public sealed record AddInferenceWorkerRequest(int Threads = 4, string ModelId = InferenceModels.DefaultModelId);

public sealed record UpdateInferenceWorkerRequest(int Threads, string ModelId);

public sealed record InferenceWorkerDesired(string WorkerId, int Threads,
    string ModelId = InferenceModels.DefaultModelId);

public sealed record InferenceWorkerObserved(string WorkerId, string State, int? Pid, int Port,
    int Threads, string? LastError, string ModelId = InferenceModels.DefaultModelId);

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
