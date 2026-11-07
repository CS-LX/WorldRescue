using System.Text;
using System.Xml.Linq;
using WorldRescue.Core;
using Xunit;

namespace WorldRescue.Tests;

public sealed class PresentationTests {
    static EntityRecord Record(string data, string component = "Container") {
        var plan = new RepairPlan(Encoding.UTF8.GetBytes($"""
            <Project><Entities>
            <Entity Id="1" Name="Sample"><Values Name="BlockEntity"><Value Name="Coordinates" Value="1,2,3"/></Values><Values Name="{component}">{data}</Values></Entity>
            <Entity Id="2" Name="Sample"><Values Name="BlockEntity"><Value Name="Coordinates" Value="1,2,3"/></Values></Entity>
            </Entities></Project>
            """));
        return plan.Conflicts[0].Records[0];
    }
    const string Slots = """
        <Values Name="Slots"><Values Name="Slot0"><Value Name="Contents" Value="45"/><Value Name="Count" Value="7"/></Values></Values>
        """;
    static RecordPresenters Inventory(bool matches = true) {
        var registry = new RecordPresenters();
        registry.Register("inventory", new HostInventoryPresenter((_, component) => matches && component == "Container").Describe);
        return registry;
    }

    [Fact] public void HostInventoryDisplaysRealItemsWithoutInferringOtherConcepts() {
        var result = Inventory().Describe(Record(Slots), "en-US");
        Assert.Equal(new ItemStack(45, 7), Assert.Single(Assert.Single(result.Sections).Items));
        Assert.False(result.HasUnexplainedContent);
    }
    [Fact] public void SimilarlyNamedModFieldsAreNotTreatedAsHostInventory() {
        var result = Inventory(false).Describe(Record(Slots + "<Value Name='Energy' Value='70'/>"), "en-US");
        Assert.Empty(result.Sections);
        Assert.True(result.HasUnexplainedContent);
    }
    [Fact] public void UnknownContentIsNeverReportedAsEmpty() {
        var result = Inventory().Describe(Record("<Value Name='Points' Value='10'/><Values Name='Essence'><Value Name='Amount' Value='90'/></Values>"), "zh-CN");
        Assert.Empty(result.Sections);
        Assert.True(result.HasUnexplainedContent);
    }
    [Fact] public void EmptyInventoryIsDifferentFromMissingOrMalformedInventory() {
        var empty = Inventory().Describe(Record("<Values Name='Slots'/>"), "en-US");
        Assert.Empty(Assert.Single(empty.Sections).Items);
        Assert.False(empty.HasUnexplainedContent);
        Assert.Empty(Inventory().Describe(Record(""), "en-US").Sections);
        var broken = Inventory().Describe(Record(Slots.Replace("Value=\"7\"", "Value=\"invalid\"")), "en-US");
        Assert.Empty(broken.Sections); Assert.True(broken.HasUnexplainedContent);
    }
    [Fact] public void ModCanDescribeAnyConceptWithItsOwnLabelAndIcon() {
        var registry = Inventory();
        registry.Register("sample.points", s => s.Component == "Container" ? new(
            [new("修行", "Textures/Example/Practice", [], [new("灵气", "125", "Textures/Example/Essence"), new("点数", "80")])],
            ["Essence", "Points"]) : null);
        var result = registry.Describe(Record(Slots + "<Value Name='Essence' Value='125'/><Value Name='Points' Value='80'/>"), "zh-CN");
        Assert.Equal(2, result.Sections.Count);
        Assert.Equal("灵气", result.Sections[1].Facts[0].Label);
        Assert.Equal("Textures/Example/Essence", result.Sections[1].Facts[0].Icon);
        Assert.False(result.HasUnexplainedContent);
    }
    [Fact] public void BrokenProviderCannotMutateRecordOrHideUnknownState() {
        EntityRecord record = Record("<Value Name='Points' Value='80'/>");
        XElement before = record.CreateSnapshot();
        var registry = new RecordPresenters();
        registry.Register("broken", s => { s.Data.RemoveNodes(); throw new Exception("Failure"); });
        string? observed = null;
        registry.Register("observer", s => { if (s.Component == "Container") observed = (string?)s.Data.Element("Value")?.Attribute("Value"); return null; });
        var result = registry.Describe(record, "en-US");
        Assert.Equal("80", observed); Assert.True(XNode.DeepEquals(before, record.CreateSnapshot()));
        Assert.True(result.HasUnexplainedContent); Assert.Empty(result.Sections);
    }
    [Fact] public void AdditionalInventoryStateIsStillMarkedUnexplained() {
        var result = Inventory().Describe(Record(Slots + "<Value Name='CustomData' Value='x'/>"), "en-US");
        Assert.Single(result.Sections); Assert.True(result.HasUnexplainedContent);
    }
    [Fact] public void FailedRegistrationDoesNotReplaceExistingProvider() {
        var registry = Inventory();
        Assert.Throws<ArgumentException>(() => registry.Register("inventory", _ => null));
        Assert.Single(registry.Describe(Record(Slots), "en-US").Sections);
        Assert.True(registry.Unregister("inventory"));
        Assert.Empty(registry.Describe(Record(Slots), "en-US").Sections);
    }
    [Fact] public void InventoryCustomTextAndDuplicateSlotsAreNotSilentlyClaimed() {
        Assert.True(Inventory().Describe(Record(Slots.Replace("</Values></Values>", "opaque</Values></Values>")), "en-US").HasUnexplainedContent);
        XElement slots = XElement.Parse(Slots);
        slots.Add(new XElement(slots.Elements().Single()));
        var result = Inventory().Describe(Record(slots.ToString()), "en-US");
        Assert.Empty(result.Sections); Assert.True(result.HasUnexplainedContent);
    }
    [Fact] public void EmptyMessageIsOwnedByProviderRatherThanGuessed() {
        var registry = new RecordPresenters();
        registry.Register("custom", s => s.Component == "Container"
            ? new([new("自定义内容", "icon", [], [], "尚未建立契约")], ["Custom"]) : null);
        var result = registry.Describe(Record("<Value Name='Custom' Value='0'/>"), "zh-CN");
        Assert.Equal("尚未建立契约", Assert.Single(result.Sections).EmptyText);
    }
}
