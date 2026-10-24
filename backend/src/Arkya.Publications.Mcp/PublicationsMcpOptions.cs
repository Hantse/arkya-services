namespace Arkya.Publications.Mcp;

public sealed class PublicationsMcpOptions
{
    public bool Enabled { get; init; }
    public string BaseUrl { get; init; } = "https://localhost:7143";
    public string AdminKey { get; init; } = "";
    public bool AllowLoopbackHttp { get; init; }

    public bool IsValid() => Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
        && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.AbsolutePath == "/"
        && (uri.Scheme == "https" || AllowLoopbackHttp && uri.Scheme == "http" && uri.IsLoopback)
        && AdminKey is { Length: >= 32 and <= 512 } && !AdminKey.Any(char.IsControl);
}
