using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;

namespace WorldRescue.Core;

public readonly record struct Cell(int X, int Y, int Z) {
    public override string ToString() => $"{X}, {Y}, {Z}";
    public static bool TryParse(string? text, out Cell point) {
        point = default;
        string[] parts = (text ?? "").Split(',');
        if (parts.Length != 3 || !int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int y)
            || !int.TryParse(parts[2], out int z)) return false;
        point = new(x, y, z);
        return true;
    }
}

public sealed class EntityRecord {
    internal XElement Node { get; }
    public string Id => (string?)Node.Attribute("Id") ?? "";
    public string Template => (string?)Node.Attribute("Name") ?? "";
    internal EntityRecord(XElement node) => Node = node;
    // Presenters receive detached copies; their interpretation can never change repair decisions.
    public XElement CreateSnapshot() => new(Node);
    internal static string? Value(XElement node, string name) => (string?)node.Elements("Value")
        .FirstOrDefault(x => (string?)x.Attribute("Name") == name)?.Attribute("Value");

    internal string Signature() {
        var copy = new XElement(Node);
        copy.Attribute("Id")?.Remove();
        // Ignore formatting and attribute order, but preserve element order and unknown text/state.
        return Canonical(copy).ToString(SaveOptions.DisableFormatting);
    }
    static XElement Canonical(XElement node) => new(node.Name,
        node.Attributes().OrderBy(x => x.Name.ToString()).Select(x => new XAttribute(x)),
        node.Nodes().Select(x => x is XElement element ? (object)Canonical(element)
            : x is XText text && string.IsNullOrWhiteSpace(text.Value) ? null! : x));
}

public sealed class Conflict {
    public Cell Position { get; }
    public IReadOnlyList<EntityRecord> Records { get; }
    public bool Equivalent => Records.Select(x => x.Signature()).Distinct().Count() == 1;
    internal Conflict(Cell position, EntityRecord[] records) { Position = position; Records = records; }
}

public sealed class RepairPlan {
    readonly XElement m_document;
    public byte[] Original { get; }
    public IReadOnlyList<Conflict> Conflicts { get; }
    public string Hash => Convert.ToHexString(SHA256.HashData(Original));

    public RepairPlan(byte[] original) {
        Original = original.ToArray();
        using var reader = XmlReader.Create(new MemoryStream(original), new XmlReaderSettings {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 128 * 1024 * 1024
        });
        m_document = XElement.Load(reader, LoadOptions.PreserveWhitespace);
        if (m_document.Name != "Project" || m_document.Element("Entities") == null)
            throw new InvalidDataException("Not a world project.");
        var grouped = new Dictionary<Cell, List<EntityRecord>>();
        foreach (XElement entity in m_document.Element("Entities")!.Elements("Entity")) {
            XElement? block = entity.Elements("Values").FirstOrDefault(x => (string?)x.Attribute("Name") == "BlockEntity");
            if (block == null || !Cell.TryParse(EntityRecord.Value(block, "Coordinates"), out Cell point) || point.Y < 0) continue;
            // Moving assemblies have a separate index in the host; coincident stationary coordinates are normal there.
            if (EntityRecord.Value(block, "MovingBlockSetPosition") != null && EntityRecord.Value(block, "MovingBlockOffset") != null) continue;
            if (!grouped.TryGetValue(point, out var records)) grouped[point] = records = [];
            records.Add(new EntityRecord(entity));
        }
        Conflicts = grouped.Where(x => x.Value.Count > 1).OrderBy(x => x.Key.X).ThenBy(x => x.Key.Y).ThenBy(x => x.Key.Z)
            .Select(x => new Conflict(x.Key, x.Value.ToArray())).ToArray();
    }

    // A choice is the record's ordinal in its conflict, never an entity ID (damaged worlds may reuse IDs).
    public byte[] Apply(IReadOnlyDictionary<Cell, int> choices) {
        if (choices.Count != Conflicts.Count || Conflicts.Any(x => !choices.TryGetValue(x.Position, out int i) || i < 0 || i >= x.Records.Count))
            throw new InvalidOperationException("Every conflict needs one explicit survivor.");
        var removed = Conflicts.SelectMany(c => c.Records.Where((_, i) => i != choices[c.Position])).ToArray();
        var removedNodes = removed.Select(x => x.Node).ToHashSet();
        var removedIds = removed.Select(x => x.Id).Where(x => x.Length > 0).ToHashSet();
        foreach (XElement value in m_document.Descendants("Value")) {
            if (value.Ancestors("Entity").Any(removedNodes.Contains)) continue;
            string name = (string?)value.Attribute("Name") ?? "";
            string type = (string?)value.Attribute("Type") ?? "";
            string data = (string?)value.Attribute("Value") ?? "";
            if ((name.Contains("Entity", StringComparison.OrdinalIgnoreCase) || type.Contains("Entity", StringComparison.OrdinalIgnoreCase))
                && data.Split([':', ';', ',', ' '], StringSplitOptions.RemoveEmptyEntries).Any(removedIds.Contains))
                throw new InvalidOperationException("A removed record is referenced by another object. Automatic repair is unsafe.");
        }
        var result = new XElement(m_document);
        XElement[] originals = m_document.Element("Entities")!.Elements("Entity").ToArray();
        XElement[] copies = result.Element("Entities")!.Elements("Entity").ToArray();
        for (int i = 0; i < originals.Length; i++) if (removedNodes.Contains(originals[i])) copies[i].Remove();
        using var output = new MemoryStream();
        result.Save(output, SaveOptions.DisableFormatting);
        byte[] bytes = output.ToArray();
        if (new RepairPlan(bytes).Conflicts.Count != 0) throw new InvalidOperationException("Unresolved conflicts remain.");
        return bytes;
    }
}
