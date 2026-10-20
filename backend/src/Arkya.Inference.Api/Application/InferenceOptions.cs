namespace Arkya.Inference.Api.Application;

public sealed class InferenceOptions
{
    public int MaxWorkers { get; set; } = 12;
    public int MaxThreadsPerWorker { get; set; } = 24;
    public int MaxTotalThreads { get; set; } = 96;
    public int HeartbeatSeconds { get; set; } = 5;
    public int OfflineAfterSeconds { get; set; } = 30;

    public bool IsValid() => MaxWorkers is >= 1 and <= 12 &&
        MaxThreadsPerWorker is >= 1 and <= 24 && MaxTotalThreads is >= 1 and <= 288 &&
        HeartbeatSeconds is >= 1 and <= 30 &&
        OfflineAfterSeconds >= HeartbeatSeconds * 3 && OfflineAfterSeconds <= 300;
}
