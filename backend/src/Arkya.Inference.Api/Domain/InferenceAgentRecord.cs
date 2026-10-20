using Arkya.ServiceDefaults.Inference;

namespace Arkya.Inference.Api.Domain;

public sealed record InferenceAgentRecord(
    string AgentId, string DisplayName, string TokenHash,
    long Revision, long Version, long ObservedRevision, int MaxWorkers, int ThreadsPerWorker,
    DateTimeOffset CreatedAt, DateTimeOffset DesiredUpdatedAt, DateTimeOffset? LastSeenAt,
    string? MachineName, string? AgentVersion, int? LogicalProcessors, long? TotalMemoryMb,
    int? DemoPort, string? LastError, InferenceWorkerDesired[] DesiredWorkers,
    InferenceWorkerObserved[] ObservedWorkers, InferenceWorkerDesired[] RetiredWorkers,
    InferenceWorkerThreadReservation[] ThreadReservations);

public sealed record InferenceWorkerThreadReservation(string WorkerId, int Threads);
