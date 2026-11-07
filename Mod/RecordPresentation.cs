using System.Xml.Linq;
using Engine.Serialization;
using Game;
using TemplatesDatabase;
using WorldRescue.Core;

namespace WorldRescue;

public sealed record ItemStack(int Value, int Count);
public sealed record RecordFact(string Label, string Value, string Icon = "Textures/WorldRescue/Remix/information-line");
public sealed record DetailSection(string Title, string Icon, IReadOnlyList<ItemStack> Items, IReadOnlyList<RecordFact> Facts, string? EmptyText = null);
public sealed record ComponentDescription(IReadOnlyList<DetailSection> Sections, IReadOnlyList<string> ExplainedChildren);
public sealed record ComponentSnapshot(string Template, string Component, string Language, XElement Data);
public sealed record RecordPresentation(IReadOnlyList<DetailSection> Sections, bool HasUnexplainedContent);

/// <summary>Optional read-only presentation API. Providers return localized player text and registered icon paths.</summary>
public sealed class RecordPresenters {
    readonly Dictionary<string, Func<ComponentSnapshot, ComponentDescription?>> m_providers = [];
    public static RecordPresenters Default { get; } = CreateDefault();
    static RecordPresenters CreateDefault() {
        var result = new RecordPresenters();
        result.Register("worldrescue.host-inventory", new HostInventoryPresenter(IsHostInventory).Describe);
        return result;
    }
    public void Register(string id, Func<ComponentSnapshot, ComponentDescription?> provider) {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(provider);
        m_providers.Add(id, provider);
    }
    public bool Unregister(string id) => m_providers.Remove(id);

    public RecordPresentation Describe(EntityRecord record, string language) {
        XElement entity = record.CreateSnapshot();
        var sections = new List<DetailSection>();
        bool unknown = entity.Elements().Any(x => x.Name != "Values")
            || entity.Attributes().Any(x => x.Name != "Id" && x.Name != "Name" && x.Name != "Guid");
        foreach (XElement component in entity.Elements("Values")) {
            string name = (string?)component.Attribute("Name") ?? "";
            var explained = new HashSet<string>();
            if (name == "BlockEntity") explained.Add("Coordinates");
            foreach (var provider in m_providers.Values.ToArray()) {
                try {
                    // Each provider has its own clone, including when another provider misbehaves.
                    ComponentDescription? description = provider(new(record.Template, name, language, new XElement(component)));
                    if (description == null || description.Sections.Count == 0) continue;
                    var copy = description.Sections.Select(s => new DetailSection(s.Title, s.Icon, s.Items.ToArray(), s.Facts.ToArray(), s.EmptyText)).ToArray();
                    if (copy.Any(s => string.IsNullOrWhiteSpace(s.Title) || s.Items.Any(i => i.Count <= 0))) continue;
                    string[] children = description.ExplainedChildren.ToArray();
                    sections.AddRange(copy);
                    explained.UnionWith(children);
                }
                catch (Exception e) { System.Diagnostics.Trace.TraceWarning($"WorldRescue presenter failed: {e}"); }
            }
            unknown |= component.Elements().Any(x => !explained.Contains((string?)x.Attribute("Name") ?? ""))
                || component.Attributes().Any(a => a.Name != "Name")
                || component.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value));
        }
        return new(sections, unknown);
    }

    static bool IsHostInventory(string template, string component) {
        ValuesDictionary? entity = DatabaseManager.FindEntityValuesDictionary(template, false);
        if (entity?.GetValue<ValuesDictionary?>(component, null) is not { } values) return false;
        string? className = values.GetValue<string?>("Class", null);
        return className != null && TypeCache.FindType(className, false, false) is { } type
            && typeof(ComponentInventoryBase).IsAssignableFrom(type);
    }
}

/// <summary>Reads only ComponentInventoryBase's documented Slots serialization, never similarly named mod data.</summary>
public sealed class HostInventoryPresenter(Func<string, string, bool> isHostInventory) {
    public ComponentDescription? Describe(ComponentSnapshot snapshot) {
        if (!isHostInventory(snapshot.Template, snapshot.Component)) return null;
        XElement? slots = snapshot.Data.Elements("Values").SingleOrDefault(x => (string?)x.Attribute("Name") == "Slots");
        // Missing state is not proof of an empty inventory.
        if (slots == null) return null;
        var items = new List<ItemStack>();
        bool fullyExplained = slots.Attributes().All(a => a.Name == "Name")
            && !slots.Nodes().OfType<XText>().Any(x => !string.IsNullOrWhiteSpace(x.Value));
        var indices = new HashSet<int>();
        foreach (XElement slot in slots.Elements()) {
            string name = (string?)slot.Attribute("Name") ?? "";
            string? Value(string key) => (string?)slot.Elements("Value").SingleOrDefault(x => (string?)x.Attribute("Name") == key)?.Attribute("Value");
            if (slot.Name != "Values" || !name.StartsWith("Slot") || !int.TryParse(name.AsSpan(4), out int index) || index < 0
                || !indices.Add(index) || !int.TryParse(Value("Contents"), out int value) || !int.TryParse(Value("Count"), out int count) || count < 0) return null;
            if (count > 0) items.Add(new(value, count));
            fullyExplained &= slot.Attributes().All(a => a.Name == "Name")
                && !slot.Nodes().OfType<XText>().Any(x => !string.IsNullOrWhiteSpace(x.Value))
                && slot.Elements().All(x => x.Name == "Value" && (string?)x.Attribute("Name") is "Contents" or "Count"
                    && !x.HasElements && string.IsNullOrWhiteSpace(x.Value)
                    && x.Attributes().All(a => a.Name == "Name" || a.Name == "Type" || a.Name == "Value"));
        }
        bool english = !snapshot.Language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        return new([new(english ? "Items" : "物品", "Textures/WorldRescue/Remix/box-3-line", items, [], english ? "No items" : "没有物品")], fullyExplained ? ["Slots"] : []);
    }
}
