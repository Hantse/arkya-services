using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Arkya.ServiceDefaults;
using MongoDB.Bson.Serialization.Attributes;

namespace Arkya.Realms.Api.Domain;

public sealed record NativePublishedRelease(string GameVersion, string SourceRevision, string SourceRunId,
    string Image, int ProtocolVersion, string BuildId)
{
    public const string ImageRepository = "ghcr.io/hantse/arkya-mmorpg-dedicated";

    [JsonIgnore, BsonIgnore]
    public long Version => GameVersion is not null && Regex.IsMatch(GameVersion, @"\A0\.1\.[1-9][0-9]{0,18}\z") &&
        long.TryParse(GameVersion.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;

    public bool IsValid() => Version > 0 && SourceRevision is not null &&
        Regex.IsMatch(SourceRevision, @"\A[0-9a-f]{40}\z") && SourceRevision != new string('0', 40) &&
        SourceRunId is not null && Regex.IsMatch(SourceRunId, @"\A[1-9][0-9]{0,23}\z") &&
        Image is not null && Regex.IsMatch(Image, @"\A" + Regex.Escape(ImageRepository) + @"@sha256:[0-9a-f]{64}\z") &&
        GameProtocol.IsSupported(ProtocolVersion, BuildId);
}

public sealed record RealmNativeReleasePin(NativePublishedRelease Release, string Kind, string ProfileId,
    string PreviousProfileSha256, string PreviousImage, string TargetProfileSha256)
{
    public bool IsValid() => Release is not null && Release.IsValid() && Kind is "Image" or "RuntimeMigration" &&
        GameProtocol.ValidId(ProfileId) && ValidHash(PreviousProfileSha256) && ValidHash(TargetProfileSha256) &&
        PreviousImage is { Length: <= 512 } && Regex.IsMatch(PreviousImage, @"\A[a-z0-9][a-z0-9._:/-]*@sha256:[0-9a-f]{64}\z");

    private static bool ValidHash(string? value) => value is not null && Regex.IsMatch(value, @"\A[0-9a-f]{64}\z");
}
