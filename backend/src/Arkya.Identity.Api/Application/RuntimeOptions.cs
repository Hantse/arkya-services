namespace Arkya.Identity.Api.Application;

public sealed class ManagementOptions
{
    public int Port { get; set; }
    public TimeSpan ReadinessTimeout { get; set; } = TimeSpan.FromSeconds(2);

    public bool IsValid() => (Port == 0 || Port is >= 1024 and <= 65535) &&
        ReadinessTimeout >= TimeSpan.FromMilliseconds(100) && ReadinessTimeout <= TimeSpan.FromSeconds(10);
}

public sealed class KeyPersistenceOptions
{
    public string KeysPath { get; set; } = "";

    public bool IsValid() => KeysPath is not null &&
        (KeysPath.Length == 0 || (KeysPath == KeysPath.Trim() && Path.IsPathFullyQualified(KeysPath)));
}
