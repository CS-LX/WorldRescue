using System.Security.Cryptography;
using System.Text.Json;

namespace WorldRescue.Core;

public sealed record RepairReceipt(string BackupDirectory, string ResultHash);

public static class RepairTransaction {
    public static RepairReceipt Commit(string worldDirectory, RepairPlan plan, IReadOnlyDictionary<Cell, int> choices) {
        string path = Path.Combine(worldDirectory, "Project.xml");
        byte[] repaired = plan.Apply(choices);
        EnsureUnchanged(path, plan.Hash);
        string backup = Path.Combine(worldDirectory, "WorldRescueBackups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        WriteNew(Path.Combine(backup, "Project.xml"), plan.Original);
        string quickBackup = Path.Combine(worldDirectory, "Project.bak");
        if (File.Exists(quickBackup)) WriteNew(Path.Combine(backup, "Project.bak"), File.ReadAllBytes(quickBackup));
        EnsureUnchanged(Path.Combine(backup, "Project.xml"), plan.Hash);
        string resultHash = Hash(repaired);
        WriteNew(Path.Combine(backup, "repair.json"), JsonSerializer.SerializeToUtf8Bytes(new {
            OriginalHash = plan.Hash, ResultHash = resultHash,
            Choices = plan.Conflicts.Select(c => new { c.Position, KeptId = c.Records[choices[c.Position]].Id,
                RemovedIds = c.Records.Where((_, i) => i != choices[c.Position]).Select(r => r.Id).ToArray() })
        }, new JsonSerializerOptions { WriteIndented = true }));
        Replace(path, repaired, plan.Hash);
        return new(backup, resultHash);
    }

    public static void Undo(string worldDirectory, RepairReceipt receipt) {
        string originalPath = Path.Combine(receipt.BackupDirectory, "Project.xml");
        using var report = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(receipt.BackupDirectory, "repair.json")));
        EnsureUnchanged(originalPath, report.RootElement.GetProperty("OriginalHash").GetString()!);
        Replace(Path.Combine(worldDirectory, "Project.xml"), File.ReadAllBytes(originalPath), receipt.ResultHash);
    }

    static void Replace(string path, byte[] bytes, string expectedHash) {
        string temporary = path + ".rescue-" + Guid.NewGuid().ToString("N");
        try {
            WriteNew(temporary, bytes);
            EnsureUnchanged(temporary, Hash(bytes));
            EnsureUnchanged(path, expectedHash);
            // Same-directory rename; never truncate the player's live project in place.
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    static void WriteNew(string path, byte[] data) {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(data);
        file.Flush(true);
    }
    static void EnsureUnchanged(string path, string hash) {
        if (Hash(File.ReadAllBytes(path)) != hash) throw new IOException("The world changed. Scan it again before repairing.");
    }
    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
