using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Arkya.Content.Api.Contracts;
using Arkya.Content.Api.Domain;

namespace Arkya.Content.Tests;

internal static class ItemFixtures
{
    public const string Guid = "1234567890abcdef1234567890abcdef";
    public const string Hash = "abcdef1234567890abcdef1234567890";
    public static ItemRequest Request(string id = "training-sword", int attack = 5) =>
        new(id, "Épée d’entraînement", "Une lame pour les nouveaux aventuriers.", "weapon", "common", "main-hand", Guid,
            1, 1800, 1, attack, 0);
    public static ItemDefinition Definition(int attack = 5) =>
        new("training-sword", "Épée d’entraînement", "Une lame pour les nouveaux aventuriers.", "weapon", "common", "main-hand",
            Guid, 1, 1800, 1, attack, 0);
    public static ItemVisualAsset Asset(string path = "Assets/Arkya/Items/Sword.prefab", string hash = Hash) =>
        new(Guid, "Sword", path, "prefab", hash, Png(), FixedTimeProvider.Now);
    public static UpsertItemAssetRequest AssetRequest(string path = "Assets/Arkya/Items/Sword.prefab", string hash = Hash) =>
        new("Sword", path, "prefab", hash, Convert.ToBase64String(Png()));

    public static byte[] Png(int width = 2, int height = 2, bool addMetadata = false, bool badFilter = false, bool extraPixels = false)
    {
        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; header[9] = 6;
        Chunk(png, "IHDR", header);
        if (addMetadata) Chunk(png, "tEXt", Encoding.ASCII.GetBytes("comment\0should be removed"));
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var row = new byte[width * 4 + 1];
            row[0] = badFilter ? (byte)5 : (byte)0;
            for (var index = 0; index < height; index++) zlib.Write(row);
            if (extraPixels) zlib.WriteByte(0);
        }
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream output, string type, byte[] content)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, content.Length); output.Write(number);
        var bytes = Encoding.ASCII.GetBytes(type).Concat(content).ToArray(); output.Write(bytes);
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ (0xedb88320u & (uint)-(int)(crc & 1));
        }
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); output.Write(number);
    }
}
