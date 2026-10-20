using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Arkya.ServiceDefaults.Reports;

namespace Arkya.Reports.Api.Domain;

public static class PngScreenshot
{
    public const int MaximumBytes = 2 * 1024 * 1024;
    public const int MaximumBase64Characters = ((MaximumBytes + 2) / 3) * 4;
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    public static bool TryRead(string? base64, out byte[]? image, out BugReportScreenshot? metadata)
    {
        image = null;
        metadata = null;
        if (base64 is null || base64.Length is < 1 or > MaximumBase64Characters) return false;
        byte[] data;
        try { data = Convert.FromBase64String(base64); }
        catch (FormatException) { return false; }
        if (data.Length is < 45 or > MaximumBytes || !data.AsSpan(0, 8).SequenceEqual(Signature)) return false;
        var width = 0;
        var height = 0;
        var channels = 0;
        var ended = false;
        var dataEnded = false;
        var dataStarted = false;
        using var compressed = new MemoryStream();
        using var clean = new MemoryStream();
        clean.Write(Signature);
        for (var offset = 8; offset < data.Length;)
        {
            if (data.Length - offset < 12) return false;
            var length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
            if (length > data.Length - offset - 12) return false;
            var count = (int)length;
            var chunk = data.AsSpan(offset + 4, count + 4);
            if (Crc32(chunk) != BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + 8 + count, 4))) return false;
            var type = Encoding.ASCII.GetString(chunk[..4]);
            var content = chunk[4..];
            switch (type)
            {
                case "IHDR":
                    if (offset != 8 || count != 13) return false;
                    var w = BinaryPrimitives.ReadUInt32BigEndian(content[..4]);
                    var h = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(4, 4));
                    if (w is < 1 or > 1920 || h is < 1 or > 1080 || content[8] != 8 ||
                        content[9] is not (2 or 6) || content[10] != 0 || content[11] != 0 || content[12] != 0) return false;
                    width = (int)w;
                    height = (int)h;
                    channels = content[9] == 2 ? 3 : 4;
                    clean.Write(data.AsSpan(offset, count + 12));
                    break;
                case "IDAT":
                    if (width == 0 || dataEnded) return false;
                    dataStarted = true;
                    compressed.Write(content);
                    clean.Write(data.AsSpan(offset, count + 12));
                    break;
                case "IEND":
                    if (!dataStarted || count != 0 || offset + 12 != data.Length) return false;
                    ended = true;
                    clean.Write(data.AsSpan(offset, 12));
                    break;
                default:
                    // Strip optional metadata before storage; animated or unknown critical PNGs are rejected.
                    if (width == 0 || type is "acTL" or "fcTL" or "fdAT" ||
                        type.Any(character => !char.IsAsciiLetter(character)) || !char.IsLower(type[0])) return false;
                    if (dataStarted) dataEnded = true;
                    break;
            }
            offset += count + 12;
        }
        if (!ended || compressed.Length == 0) return false;
        compressed.Position = 0;
        try
        {
            using var decoded = new ZLibStream(compressed, CompressionMode.Decompress);
            var row = new byte[width * channels + 1];
            for (var y = 0; y < height; y++)
            {
                decoded.ReadExactly(row);
                if (row[0] > 4) return false;
            }
            if (decoded.ReadByte() != -1) return false;
        }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException or IOException)
        { return false; }
        image = clean.ToArray();
        metadata = new(width, height, image.Length, "image/png", Convert.ToHexStringLower(SHA256.HashData(image)));
        return true;
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ (0xedb88320u & (uint)-(int)(crc & 1));
        }
        return ~crc;
    }
}
