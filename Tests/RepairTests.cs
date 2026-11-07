using System.Text;
using System.Xml.Linq;
using Game;
using GameEntitySystem;
using WorldRescue.Core;
using Xunit;

namespace WorldRescue.Tests;

public sealed class RepairTests : IDisposable {
    readonly string m_directory = Path.Combine(Path.GetTempPath(), "WorldRescueTests-" + Guid.NewGuid());
    static readonly Cell Point = new(-42, 34, 1893);
    public RepairTests() => Directory.CreateDirectory(m_directory);
    public void Dispose() => Directory.Delete(m_directory, true);

    static XElement Entity(string id, string coordinate = "-42,34,1893", string state = "") => XElement.Parse($"""
        <Entity Id="{id}" Name="Chest" Guid="00000000-0000-0000-0000-000000000001">
          <Values Name="BlockEntity"><Value Name="Coordinates" Type="Point3" Value="{coordinate}" /></Values>
          <Values Name="Chest">{state}</Values>
        </Entity>
        """);
    static byte[] World(params XElement[] entities) => Encoding.UTF8.GetBytes(new XElement("Project",
        new XAttribute("Version", "2.4"), new XElement("Subsystems", new XElement("Values", new XAttribute("Name", "Unrelated"))),
        new XElement("Entities", entities)).ToString());
    static Dictionary<Cell, int> Choose(int index) => new() { [Point] = index };

    [Fact] public void DetectsNumericallyEqualCoordinatesAndPreservesOtherEntities() {
        byte[] source = World(Entity("1"), Entity("2", " -42, 34, 1893 "), Entity("3", "0,1,0"));
        var plan = new RepairPlan(source);
        Assert.Equal(Point, Assert.Single(plan.Conflicts).Position);
        byte[] result = plan.Apply(Choose(1));
        XElement root = XElement.Load(new MemoryStream(result));
        Assert.Equal(new[] { "2", "3" }, root.Element("Entities")!.Elements().Select(e => (string)e.Attribute("Id")!));
        Assert.True(XNode.DeepEquals(XElement.Parse(Encoding.UTF8.GetString(source)).Element("Subsystems"), root.Element("Subsystems")));
        Assert.Equal(source, plan.Original);
    }

    [Fact] public void UnknownStateIsPreservedAndPreventsIdenticalSuggestion() {
        var a = Entity("1", state: "<Values Name='Unknown'><Value Name='Payload' Type='string' Value='A'/></Values>");
        var b = Entity("2", state: "<Values Name='Unknown'><Value Name='Payload' Type='string' Value='B'/></Values>");
        var plan = new RepairPlan(World(a, b));
        Assert.False(plan.Conflicts[0].Equivalent);
        XElement kept = XElement.Load(new MemoryStream(plan.Apply(Choose(1)))).Element("Entities")!.Elements().Single();
        Assert.True(XNode.DeepEquals(b, kept));
    }

    [Fact] public void IdenticalComparisonIgnoresIdsAndOrdering() {
        var a = Entity("1"); var b = Entity("2");
        b.ReplaceAttributes(b.Attributes().Reverse().ToArray());
        Assert.True(new RepairPlan(World(a, b)).Conflicts[0].Equivalent);
    }

    [Fact] public void UnknownTextAndSequenceOrderPreventSuggestion() {
        Assert.False(new RepairPlan(World(Entity("1", state: "<Custom>A</Custom>"), Entity("2", state: "<Custom>B</Custom>"))).Conflicts[0].Equivalent);
        Assert.False(new RepairPlan(World(Entity("1", state: "<Custom><Entry>A</Entry><Entry>B</Entry></Custom>"),
            Entity("2", state: "<Custom><Entry>B</Entry><Entry>A</Entry></Custom>"))).Conflicts[0].Equivalent);
    }

    [Fact] public void SnapshotIsDetachedFromRepairData() {
        var plan = new RepairPlan(World(Entity("1", state: """
            <Value Name="CustomPoints" Value="125"/>
            """), Entity("2")));
        var snapshot = plan.Conflicts[0].Records[0].CreateSnapshot();
        snapshot.RemoveNodes();
        Assert.Contains("125", Encoding.UTF8.GetString(plan.Apply(Choose(0))));
        Assert.False(plan.Conflicts[0].Equivalent);
    }

    [Fact] public void MovingBlocksAndNegativeHeightAreNotStaticConflicts() {
        var moving = Entity("2");
        moving.Element("Values")!.Add(XElement.Parse("<Value Name='MovingBlockSetPosition' Value='1,2,3'/>"), XElement.Parse("<Value Name='MovingBlockOffset' Value='0,0,0'/>"));
        Assert.Empty(new RepairPlan(World(Entity("1"), moving, Entity("3", "0,-1,0"), Entity("4", "0,-1,0"))).Conflicts);
    }

    [Fact] public void DuplicateEntityIdsStillSelectByOrdinal() {
        var plan = new RepairPlan(World(Entity("1"), Entity("1", state: "<Value Name='Capacity' Value='64'/>")));
        Assert.Contains("Capacity", Encoding.UTF8.GetString(plan.Apply(Choose(1))));
        Assert.Single(XElement.Load(new MemoryStream(plan.Apply(Choose(0)))).Element("Entities")!.Elements());
    }

    [Fact] public void RequiresEveryChoiceAndRejectsInvalidIndices() {
        var plan = new RepairPlan(World(Entity("1"), Entity("2")));
        Assert.Throws<InvalidOperationException>(() => plan.Apply(new Dictionary<Cell, int>()));
        Assert.Throws<InvalidOperationException>(() => plan.Apply(Choose(-1)));
        Assert.Throws<InvalidOperationException>(() => plan.Apply(Choose(2)));
    }

    [Fact] public void MultipleConflictsAreResolvedTogether() {
        var plan = new RepairPlan(World(Entity("1"), Entity("2"), Entity("3", "1,2,3"), Entity("4", "1,2,3")));
        var choices = Choose(0); choices[new(1, 2, 3)] = 1;
        Assert.Empty(new RepairPlan(plan.Apply(choices)).Conflicts);
    }

    [Fact] public void ReferencedRecordCannotBeSilentlyDeleted() {
        var plan = new RepairPlan(World(Entity("1"), Entity("2"), Entity("3", "1,2,3", "<Value Name='TargetEntityId' Value='1'/>")));
        Assert.Throws<InvalidOperationException>(() => plan.Apply(Choose(1)));
        Assert.Empty(new RepairPlan(plan.Apply(Choose(0))).Conflicts);
    }

    [Fact] public void CommitBacksUpExactBytesAndUndoRestoresThem() {
        byte[] original = World(Entity("1"), Entity("2"));
        File.WriteAllBytes(Path.Combine(m_directory, "Project.xml"), original);
        File.WriteAllText(Path.Combine(m_directory, "Project.bak"), "original backup");
        File.WriteAllText(Path.Combine(m_directory, "terrain.dat"), "unchanged");
        RepairReceipt receipt = RepairTransaction.Commit(m_directory, new(original), Choose(1));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(receipt.BackupDirectory, "Project.xml")));
        Assert.Equal("original backup", File.ReadAllText(Path.Combine(receipt.BackupDirectory, "Project.bak")));
        Assert.Empty(new RepairPlan(File.ReadAllBytes(Path.Combine(m_directory, "Project.xml"))).Conflicts);
        Assert.Equal("unchanged", File.ReadAllText(Path.Combine(m_directory, "terrain.dat")));
        RepairTransaction.Undo(m_directory, receipt);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(m_directory, "Project.xml")));
    }

    [Fact] public void ConcurrentChangePreventsCommitAndUndo() {
        byte[] original = World(Entity("1"), Entity("2"));
        string path = Path.Combine(m_directory, "Project.xml");
        File.WriteAllBytes(path, original);
        var plan = new RepairPlan(original);
        File.AppendAllText(path, " ");
        Assert.Throws<IOException>(() => RepairTransaction.Commit(m_directory, plan, Choose(0)));
        File.WriteAllBytes(path, original);
        var receipt = RepairTransaction.Commit(m_directory, plan, Choose(0));
        File.AppendAllText(path, " ");
        byte[] newer = File.ReadAllBytes(path);
        Assert.Throws<IOException>(() => RepairTransaction.Undo(m_directory, receipt));
        Assert.Equal(newer, File.ReadAllBytes(path));
    }

    [Fact] public void BackupFailureLeavesOriginalUntouched() {
        byte[] original = World(Entity("1"), Entity("2"));
        string path = Path.Combine(m_directory, "Project.xml");
        File.WriteAllBytes(path, original);
        File.WriteAllText(Path.Combine(m_directory, "WorldRescueBackups"), "blocks directory creation");
        Assert.Throws<IOException>(() => RepairTransaction.Commit(m_directory, new(original), Choose(0)));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact] public void RepairedEntitiesRegisterWithHostWithoutDuplicateKey() {
        var plan = new RepairPlan(World(Entity("1"), Entity("2")));
        Assert.Throws<ArgumentException>(() => Register(plan.Original));
        Register(plan.Apply(Choose(1)));
        static void Register(byte[] bytes) {
            var index = new SubsystemBlockEntities();
            foreach (XElement entity in XElement.Load(new MemoryStream(bytes)).Element("Entities")!.Elements()) {
                Cell.TryParse((string?)entity.Descendants("Value").First().Attribute("Value"), out Cell p);
                var block = new ComponentBlockEntity { Coordinates = new(p.X, p.Y, p.Z) };
                var runtime = new Entity { m_components = [block] }; block.m_entity = runtime;
                index.OnEntityAdded(runtime);
            }
        }
    }

    [Fact] public void RejectsExternalEntities() => Assert.ThrowsAny<Exception>(() => new RepairPlan(Encoding.UTF8.GetBytes("<!DOCTYPE Project [<!ENTITY e SYSTEM 'file:///secret'>]><Project><Entities>&e;</Entities></Project>")));
}
