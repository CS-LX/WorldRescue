using System.Xml.Linq;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Game;

static class ModIconPreview {
    public static void Render(string modRoot, string output) {
        var mod = new ModEntity();
        mod.LoadIcon(File.OpenRead(Path.Combine(modRoot, "icon.png")));
        using var target = new RenderTarget2D(680, 160, 1, ColorFormat.Rgba8888, DepthFormat.Depth24Stencil8);
        Display.RenderTarget = target; Display.Viewport = new(0, 0, 680, 160); Display.ScissorRectangle = new(0, 0, 680, 160);
        Display.Clear(new Color(48, 48, 44), 1, 0);
        var root = new CanvasWidget();
        Widget row = Widget.LoadWidget(null, ContentManager.Get<XElement>("Widgets/ModsManageContentItem"), null);
        var container = (ContainerWidget)row;
        container.Children.Find<RectangleWidget>("ModsManageContentItem.Icon").Subtexture = new(mod.Icon, Vector2.Zero, Vector2.One);
        container.Children.Find<LabelWidget>("ModsManageContentItem.Title").Text = "世界修复助手";
        using var metadata = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(modRoot, "modinfo.json")));
        container.Children.Find<LabelWidget>("ModsManageContentItem.Information").Text = metadata.RootElement.GetProperty("Version").GetString() ?? "";
        container.Children.Find<LabelWidget>("ModsManageContentItem.Description").Text = "查看重叠位置，选择保留内容，备份后修复";
        root.Children.Add(row); root.SetWidgetPosition(row, new(24, 30));
        Widget.LayoutWidgetsHierarchy(root, new(680, 160)); Widget.DrawWidgetsHierarchy(root);
        RenderTarget2D.Save(target, Path.Combine(output, "mod-icon-in-game.png"), ImageFileFormat.Png, true);
        Display.RenderTarget = null;
        mod.Icon.Dispose();
        Console.WriteLine("Mod icon decoded by host ModEntity and rendered at native 64px in ModsManageContentItem.");
    }
}
