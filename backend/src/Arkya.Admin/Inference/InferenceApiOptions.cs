namespace Arkya.Admin.Inference;

public sealed class InferenceApiOptions
{
    public bool Enabled { get; init; }
    public string BaseUrl { get; init; } = "https://localhost:7146";
    public string AdminKey { get; init; } = "";
    public bool AllowClusterHttp { get; init; }
    public string CertificateAuthorityFile { get; init; } = "";
}
