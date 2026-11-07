using System.IO.Compression;

namespace WorldRescue.Core;

public sealed record PreviewCell(Cell Position, int Value);
public sealed record Neighborhood(Cell Center, IReadOnlyList<PreviewCell> Cells, int MissingChunks);

// Read-only 2.3/2.4 RGN1 reader. It never creates chunks, changes terrain or opens a world Project.
public static class RegionReader {
    public static Neighborhood Read(string worldDirectory, Cell center, int radius = 3) {
        if (radius is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(radius));
        var chunks = new Dictionary<(int, int), int[]?>();
        for (int cx = (center.X - radius) >> 4; cx <= (center.X + radius) >> 4; cx++)
            for (int cz = (center.Z - radius) >> 4; cz <= (center.Z + radius) >> 4; cz++)
                chunks[(cx, cz)] = ReadChunk(worldDirectory, cx, cz);
        var cells = new List<PreviewCell>();
        for (int y = Math.Max(0, center.Y - radius); y <= Math.Min(255, center.Y + radius); y++)
            for (int z = center.Z - radius; z <= center.Z + radius; z++)
                for (int x = center.X - radius; x <= center.X + radius; x++) {
                    int[]? chunk = chunks[(x >> 4, z >> 4)];
                    if (chunk == null) continue;
                    int value = chunk[(x & 15) + (z & 15) * 16 + y * 256];
                    if ((value & 1023) != 0) cells.Add(new(new(x, y, z), value));
                }
        return new(center, cells, chunks.Values.Count(x => x == null));
    }

    static int[]? ReadChunk(string directory, int cx, int cz) {
        string path = Path.Combine(directory, "Regions", $"Region {cx >> 4},{cz >> 4}.dat");
        if (!File.Exists(path)) return null;
        using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(file);
        if (reader.ReadUInt32() != 0x314E4752 || file.Length < 2052) throw new InvalidDataException("Unsupported region.");
        file.Position = 4 + ((cx & 15) + (cz & 15) * 16) * 8;
        int offset = reader.ReadInt32(), length = reader.ReadInt32();
        if (offset == 0 && length == 0) return null;
        if (offset < 2052 || length <= 0 || length > 1048576 || (long)offset + length + 4 > file.Length)
            throw new InvalidDataException("Invalid region entry.");
        file.Position = offset;
        if (reader.ReadUInt32() != 0x314B4843) throw new InvalidDataException("Invalid chunk.");
        return Decode(reader.ReadBytes(length));
    }

    public static int[] Decode(byte[] compressed) {
        using var input = new DeflateStream(new MemoryStream(compressed), CompressionMode.Decompress);
        // Maximum is 256 climate bytes plus one 4-byte RLE entry per cell.
        byte[] buffer = new byte[262401];
        int length = 0, read;
        while (length < buffer.Length && (read = input.Read(buffer, length, buffer.Length - length)) > 0) length += read;
        if (length < 260 || length == buffer.Length) throw new InvalidDataException("Invalid chunk length.");
        int cursor = 256, cell = 0;
        int[] result = new int[65536];
        while (cursor < length) {
            if (cursor + 4 > length) throw new InvalidDataException("Truncated run.");
            int value = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(cursor, 4));
            cursor += 4;
            int light = (value >> 10) & 15;
            if (light == 15 && cursor >= length) throw new InvalidDataException("Truncated long run.");
            int count = light < 15 ? light + 1 : buffer[cursor++] + 16;
            if (cell + count > result.Length) throw new InvalidDataException("Chunk overflow.");
            Array.Fill(result, value & ~15360, cell, count);
            cell += count;
        }
        if (cell != result.Length) throw new InvalidDataException("Incomplete chunk.");
        return result;
    }
}
