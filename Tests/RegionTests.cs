using System.IO.Compression;
using WorldRescue.Core;
using Xunit;

namespace WorldRescue.Tests;

public class RegionTests {
    static byte[] Compress(byte[] raw) {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, true)) deflate.Write(raw);
        return output.ToArray();
    }
    [Fact] public void DecodesRunsAndRemovesStoredLightBits() {
        using var raw = new MemoryStream(); raw.Write(new byte[256]);
        using var writer = new BinaryWriter(raw);
        for (int remaining = 65536; remaining > 0;) {
            int count = Math.Min(271, remaining); remaining -= count;
            writer.Write(45 | (count < 16 ? count - 1 : 15) << 10);
            if (count >= 16) writer.Write((byte)(count - 16));
        }
        int[] cells = RegionReader.Decode(Compress(raw.ToArray()));
        Assert.Equal(65536, cells.Length); Assert.All(cells, x => Assert.Equal(45, x));
    }
    [Fact] public void RejectsIncompleteAndOverlongCompressedData() {
        Assert.Throws<InvalidDataException>(() => RegionReader.Decode(Compress(new byte[260])));
        Assert.Throws<InvalidDataException>(() => RegionReader.Decode(Compress(new byte[300000])));
    }
    [Fact] public void MissingRegionsStayReadOnlyAndAreReported() {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Neighborhood result = RegionReader.Read(path, new(-1, 34, -1));
        Assert.Equal(4, result.MissingChunks); Assert.Empty(result.Cells); Assert.False(Directory.Exists(path));
    }
}
