using System.Text.Json;

namespace Arkya.ServiceDefaults.Inference;

public static class InferenceModels
{
    public const string DefaultModelId = "lfm";
    public const string Lfm = "hf.co/LiquidAI/LFM2.5-1.2B-Instruct-GGUF:Q4_K_M";
    public static IReadOnlyList<InferenceModelOption> Catalog { get; } = Load();

    public static bool IsAllowed(string? modelId) => Catalog.Any(model => model.ModelId == modelId);

    private static IReadOnlyList<InferenceModelOption> Load()
    {
        using var stream = typeof(InferenceModels).Assembly.GetManifestResourceStream("Arkya.InferenceModels.json")
            ?? throw new InvalidOperationException("The inference model catalog is missing.");
        using var document = JsonDocument.Parse(stream);
        return Array.AsReadOnly(document.RootElement.GetProperty("models").EnumerateArray().Select(model =>
            new InferenceModelOption(model.GetProperty("modelId").GetString()!,
                model.GetProperty("label").GetString()!, model.GetProperty("ollamaModel").GetString()!)).ToArray());
    }
}
