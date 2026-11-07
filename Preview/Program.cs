using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Game;
using WorldRescue;
using WorldRescue.Core;

string modRoot = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
// Optional real-world fixture is extracted only into a new directory under the preview output.
bool iconOnly = args.Skip(2).Contains("--icon-only");
if (args.Length > 2 && !iconOnly) {
    string fixture = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N"));
    ZipFile.ExtractToDirectory(args[2], fixture);
    var original = File.ReadAllBytes(Path.Combine(fixture, "Project.xml"));
    var plan = new RepairPlan(original);
    Console.WriteLine($"Real fixture: {plan.Conflicts.Count} conflicts.");
    foreach (Conflict conflict in plan.Conflicts) {
        var scene = RegionReader.Read(fixture, conflict.Position);
        Console.WriteLine($"{conflict.Position}: {conflict.Records.Count} records, {scene.Cells.Count} nearby blocks, {scene.MissingChunks} missing chunks.");
        if (scene.Cells.Count == 0 || scene.MissingChunks != 0) throw new Exception("Real terrain was not decoded.");
    }
    var choices = plan.Conflicts.ToDictionary(c => c.Position, c => c.Records.Count - 1);
    var receipt = RepairTransaction.Commit(fixture, plan, choices);
    if (new RepairPlan(File.ReadAllBytes(Path.Combine(fixture, "Project.xml"))).Conflicts.Count != 0) throw new Exception("Repair failed.");
    RepairTransaction.Undo(fixture, receipt);
    if (!original.SequenceEqual(File.ReadAllBytes(Path.Combine(fixture, "Project.xml")))) throw new Exception("Undo changed bytes.");
    Console.WriteLine("Real fixture repair/undo verified; supplied archive was not modified.");
}

bool rendered = false;
Window.Frame += () => {
    if (rendered) return;
    rendered = true;
    try {
        ContentManager.Initialize();
        new SurvivalCraftModEntity().CombineContent();
        foreach (string path in Directory.EnumerateFiles(Path.Combine(modRoot, "Assets"), "*", SearchOption.AllDirectories)) {
            var content = new ContentInfo(Path.GetRelativePath(Path.Combine(modRoot, "Assets"), path).Replace('\\', '/'));
            content.SetContentStream(new MemoryStream(File.ReadAllBytes(path))); ContentManager.Add(content);
        }
        TextureAtlasManager.Initialize(); LightingManager.Initialize(); BlocksTexturesManager.Initialize(); BlocksManager.CalculateSlotTexCoordTables();
        if (iconOnly) { ModIconPreview.Render(modRoot, output); return; }
        BlocksManager.Blocks[45] = new ChestBlock { BlockIndex = 45, DefaultDisplayName = "Chest" };
        BlocksManager.Blocks[2] = new DirtBlock { BlockIndex = 2, DefaultDisplayName = "Dirt", DefaultTextureSlot = 2 };
        BlocksManager.Blocks[5] = new CobblestoneBlock { BlockIndex = 5, DefaultDisplayName = "Cobblestone", DefaultTextureSlot = 16 };
        BlocksManager.Blocks[0] = new AirBlock();
        // Use the same host component metadata contract as a loaded database, without creating a world.
        var chest = new TemplatesDatabase.ValuesDictionary();
        chest.SetValue("Class", typeof(ComponentChest).FullName!);
        var template = new TemplatesDatabase.ValuesDictionary();
        template.SetValue("Chest", chest);
        DatabaseManager.m_valueDictionaries["Chest"] = template;
        string directory = Path.Combine(output, "sample"); Directory.CreateDirectory(directory);
        byte[] sample = Encoding.UTF8.GetBytes("""
            <Project Version="2.4"><Subsystems/><Entities>
            <Entity Id="1" Name="Chest"><Values Name="BlockEntity"><Value Name="Coordinates" Type="Point3" Value="8,64,8"/></Values><Values Name="Chest"><Values Name="Slots"/></Values></Entity>
            <Entity Id="2" Name="Chest"><Values Name="BlockEntity"><Value Name="Coordinates" Type="Point3" Value="8,64,8"/></Values><Values Name="Chest"><Values Name="Slots"><Values Name="Slot0"><Value Name="Contents" Value="5"/><Value Name="Count" Value="32"/></Values><Values Name="Slot1"><Value Name="Contents" Value="2"/><Value Name="Count" Value="16"/></Values><Values Name="Slot2"><Value Name="Contents" Value="45"/><Value Name="Count" Value="1"/></Values></Values></Values></Entity>
            </Entities></Project>
            """);
        File.WriteAllBytes(Path.Combine(directory, "Project.xml"), sample);
        WriteSampleRegion(directory);
        DialogRenderingChecks.Verify(directory, sample, output);
        foreach (string language in new[] { "zh-CN", "en-US" }) {
            LanguageControl.CurrentLanguageName = language;
            BlocksManager.Blocks[45].DefaultDisplayName = language == "zh-CN" ? "箱子" : "Chest";
            BlocksManager.Blocks[5].DefaultDisplayName = language == "zh-CN" ? "圆石" : "Cobblestone";
            BlocksManager.Blocks[2].DefaultDisplayName = language == "zh-CN" ? "泥土" : "Dirt";
            foreach ((int width, int height) in new[] { (1024, 768), (900, 600) }) {
                var dialog = new RescueDialog(directory, new RepairPlan(sample), () => { }, () => { }) { WidgetsHierarchyInput = new WidgetInput() };
                // Pump the real preview task and update bindings, without opening a gameplay world.
                for (int i = 0; i < 100; i++) { dialog.Update(); if (dialog.Children.Find<LabelWidget>("BlockName").Text.Contains(language == "zh-CN" ? "箱子" : "Chest")) break; Thread.Sleep(5); }
                using var target = new RenderTarget2D(width, height, 1, ColorFormat.Rgba8888, DepthFormat.Depth24Stencil8);
                Display.RenderTarget = target; Display.Viewport = new(0, 0, width, height); Display.ScissorRectangle = new(0, 0, width, height);
                Render(dialog, width, height, Path.Combine(output, $"preview-{language}-{width}.png"));
                Click(dialog, dialog.Children.Find<ButtonWidget>("Record1"));
                dialog.Update();
                if (dialog.Children.Find<ButtonWidget>("Apply").IsEnabled) throw new Exception("Inspecting a record selected it.");
                Render(dialog, width, height, Path.Combine(output, $"inspect-{language}-{width}.png"));
                Click(dialog, dialog.Children.Find<ButtonWidget>("Keep"));
                dialog.Update();
                if (!dialog.Children.Find<ButtonWidget>("Apply").IsEnabled) throw new Exception("Choice did not enable repair.");
                Render(dialog, width, height, Path.Combine(output, $"selected-{language}-{width}.png"));
                Click(dialog, dialog.Children.Find<ButtonWidget>("Context"));
                Render(dialog, width, height, Path.Combine(output, $"isolated-{language}-{width}.png"));
                if (dialog.Children.Find<NeighborhoodWidget>("Preview").UnavailableModels != 0) throw new Exception("Sample models failed to render.");
            }
        }
        VerifyIconBlending(output);
        RenderExtensionExamples(directory, sample, output);
        VerifyRepairFlow(directory, sample);
        Display.RenderTarget = null;
        Console.WriteLine("Actual game widget rendering verified at 1024x768 and 900x600 in Chinese/English. Selection and surroundings toggle exercised.");
    }
    catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
    finally { Window.Close(); }
};
Window.Run(64, 64, WindowMode.Fixed, "World Rescue preview");

static void Click(RescueDialog dialog, ButtonWidget button) {
    var clickable = button is RescueIconButtonWidget icon ? icon.Clickable : ((BevelledButtonWidget)button).m_clickableWidget;
    clickable.IsClicked = true; dialog.Update(); clickable.IsClicked = false;
}
static void VerifyIconBlending(string output) {
    using var target = new RenderTarget2D(256, 80, 1, ColorFormat.Rgba8888, DepthFormat.Depth24Stencil8);
    foreach (int background in new[] { 64, 192 }) foreach (int alpha in new[] { 128, 255 }) {
        Display.RenderTarget = target; Display.Viewport = new(0, 0, 256, 80); Display.ScissorRectangle = new(0, 0, 256, 80);
        Display.Clear(new Color(background, background, background), 1, 0);
        var root = new CanvasWidget();
        int x = 4;
        foreach (int size in new[] { 16, 24, 32, 48 }) {
            var icon = new RescueIconWidget { Size = new(size), Color = new(255, 255, 255, alpha),
                Subtexture = ContentManager.Get<Subtexture>("Textures/WorldRescue/Remix/question-line") };
            root.Children.Add(icon); root.SetWidgetPosition(icon, new(x, 10)); x += size + 16;
        }
        Widget.LayoutWidgetsHierarchy(root, new(256, 80)); Widget.DrawWidgetsHierarchy(root);
        var pixels = new Color[256 * 80]; target.GetData(pixels, 0, new(0, 0, 256, 80));
        int maximum = background + (int)Math.Ceiling((255 - background) * alpha / 255f);
        if (pixels.Any(p => p.R < background - 1 || p.G < background - 1 || p.B < background - 1))
            throw new Exception("Icon has a dark fringe.");
        if (pixels.Any(p => p.R > maximum + 2)) throw new Exception("Icon alpha was blended incorrectly.");
        if (!pixels.Any(p => p.R > background + 4 && p.R < maximum - 4)) throw new Exception("Icon antialiasing was clipped.");
        RenderTarget2D.Save(target, Path.Combine(output, $"icon-alpha-{background}-{alpha}.png"), ImageFileFormat.Png, true);
    }
    Console.WriteLine("GPU icon readback: no dark fringes at 16/24/32/48 px on dark/light backgrounds; half-opacity and continuous antialiasing verified.");
}
static void RenderExtensionExamples(string directory, byte[] sample, string output) {
    LanguageControl.CurrentLanguageName = "zh-CN";
    BlocksManager.Blocks[45].DefaultDisplayName = "箱子";
    XElement document = XElement.Parse(Encoding.UTF8.GetString(sample));
    document.Element("Entities")!.Elements().Last().Add(XElement.Parse("""
        <Values Name="Practice"><Value Name="Essence" Value="125"/><Value Name="Points" Value="80"/></Values>
        """));
    byte[] example = Encoding.UTF8.GetBytes(document.ToString());
    using var target = new RenderTarget2D(1024, 768, 1, ColorFormat.Rgba8888, DepthFormat.Depth24Stencil8);
    Display.RenderTarget = target; Display.Viewport = new(0, 0, 1024, 768); Display.ScissorRectangle = new(0, 0, 1024, 768);
    void Screenshot(string name) {
        var dialog = new RescueDialog(directory, new RepairPlan(example), () => { }, () => { }) { WidgetsHierarchyInput = new WidgetInput() };
        for (int i = 0; i < 100; i++) { dialog.Update(); if (dialog.Children.Find<LabelWidget>("BlockName").Text == "箱子") break; Thread.Sleep(5); }
        Click(dialog, dialog.Children.Find<ButtonWidget>("Record1"));
        Render(dialog, 1024, 768, Path.Combine(output, name + ".png"));
    }
    Screenshot("unknown-content");
    // A demonstration adapter belongs only to this preview executable, never the distributed mod.
    RecordPresenters.Default.Register("preview.practice", s => {
        if (s.Component != "Practice") return null;
        string Value(string name) => (string?)s.Data.Elements("Value").Single(v => (string?)v.Attribute("Name") == name).Attribute("Value") ?? "";
        return new ComponentDescription([
            new("修行", "Textures/WorldRescue/Remix/sparkling-line", [], [
                new("灵气", Value("Essence"), "Textures/WorldRescue/Remix/sparkling-line"),
                new("点数", Value("Points"), "Textures/WorldRescue/Remix/copper-coin-line")
            ])], ["Essence", "Points"]);
    });
    try { Screenshot("extension-content"); }
    finally { RecordPresenters.Default.Unregister("preview.practice"); }
    Console.WriteLine("Unknown-content fallback and optional third-party presentation rendered without changing repair semantics.");
}
static void VerifyRepairFlow(string directory, byte[] original) {
    LanguageControl.CurrentLanguageName = "zh-CN";
    var root = new CanvasWidget { WidgetsHierarchyInput = new WidgetInput() };
    ScreensManager.RootWidget = root;
    bool entered = false, cancelled = false;
    var dialog = new RescueDialog(directory, new RepairPlan(original), () => entered = true, () => cancelled = true);
    DialogsManager.ShowDialog(root, dialog);
    Click(dialog, dialog.Children.Find<ButtonWidget>("Record1"));
    Click(dialog, dialog.Children.Find<ButtonWidget>("Keep"));
    dialog.Update();
    Click(dialog, dialog.Children.Find<ButtonWidget>("Apply"));
    MessageDialog confirmation = DialogsManager.Dialogs.OfType<MessageDialog>().Last();
    confirmation.Dismiss(MessageDialogButton.Button2);
    if (!original.SequenceEqual(File.ReadAllBytes(Path.Combine(directory, "Project.xml")))) throw new Exception("Review changed the world.");
    Click(dialog, dialog.Children.Find<ButtonWidget>("Apply"));
    DialogsManager.Dialogs.OfType<MessageDialog>().Last().Dismiss(MessageDialogButton.Button1);
    for (int i = 0; i < 400 && !DialogsManager.Dialogs.OfType<MessageDialog>().Any(); i++) { dialog.Update(); Thread.Sleep(5); }
    var success = DialogsManager.Dialogs.OfType<MessageDialog>().Last();
    if (success.m_largeLabelWidget.Text != "修复完成，原始备份已保存") throw new Exception("Repair UI failed.");
    if (new RepairPlan(File.ReadAllBytes(Path.Combine(directory, "Project.xml"))).Conflicts.Count != 0) throw new Exception("UI did not repair.");
    success.Dismiss(MessageDialogButton.Button2);
    if (entered || !cancelled || !original.SequenceEqual(File.ReadAllBytes(Path.Combine(directory, "Project.xml")))) throw new Exception("UI undo failed.");

    // Exercise the real screen interception and preflight gate. Stop before gameplay starts.
    Screen screen = new GameLoadingScreen();
    new RescueModLoader().OnSwitchScreen(ref screen, []);
    if (screen is not RescueLoadingScreen gate) throw new Exception("Loading interception failed.");
    root.Children.Add(gate); ScreensManager.CurrentScreen = gate;
    // Preflight only consumes the directory; world-palette/gameplay initialization is outside this harness.
    var world = (WorldInfo)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(WorldInfo));
    world.DirectoryName = directory;
    gate.Enter([world, null!]);
    gate.m_stateMachine.TransitionTo("Loading");
    for (int i = 0; i < 400 && !DialogsManager.Dialogs.OfType<RescueDialog>().Any(); i++) { gate.Update(); Thread.Sleep(5); }
    if (!DialogsManager.Dialogs.OfType<RescueDialog>().Any() || GameManager.Project != null) throw new Exception("Gate did not stop before entity creation.");
    Console.WriteLine("Load interception, review-without-write, repair confirmation, backup and immediate undo exercised through real dialogs. Gameplay loading was intentionally not started.");
}
static void Render(RescueDialog dialog, int width, int height, string path) {
    Display.Clear(new Color(18, 22, 28), 1, 0);
    var root = new CanvasWidget();
    dialog.ParentWidget?.Children.Remove(dialog);
    root.Children.Add(dialog);
    Widget.LayoutWidgetsHierarchy(root, new(width, height));
    foreach (BevelledButtonWidget button in dialog.AllChildren.OfType<BevelledButtonWidget>()) {
        Vector2 text = button.m_labelWidget.Font.MeasureText(button.Text, new(button.FontScale), button.m_labelWidget.FontSpacing);
        if (text.X > button.ActualSize.X - 8 || text.Y > button.ActualSize.Y - 6) {
            for (Widget? w = button; w != null; w = w.ParentWidget) Console.WriteLine($"{w.GetType().Name}/{w.Name}: desired={w.DesiredSize} actual={w.ActualSize}");
            throw new Exception($"Button text overflow: {button.Text}: {text} / {button.ActualSize}");
        }
    }
    if (dialog.Children.Find<NeighborhoodWidget>("Preview").ActualSize.Y < 150) throw new Exception("Preview too short.");
    if (dialog.Children.Find<ButtonWidget>("Apply").GlobalBounds.Max.Y > height) throw new Exception("Footer clipped.");
    Widget.DrawWidgetsHierarchy(root);
    RenderTarget2D.Save((RenderTarget2D)Display.RenderTarget!, path, ImageFileFormat.Png, true);
}
static void WriteSampleRegion(string directory) {
    int[] cells = new int[65536];
    for (int z = 5; z <= 11; z++) for (int x = 5; x <= 11; x++) cells[x + z * 16 + 63 * 256] = 2;
    for (int y = 64; y <= 66; y++) for (int x = 5; x <= 11; x++) cells[x + 5 * 16 + y * 256] = 5;
    cells[8 + 8 * 16 + 64 * 256] = 45;
    using var data = new MemoryStream();
    using (var deflate = new DeflateStream(data, CompressionLevel.Fastest, true)) {
        using var writer = new BinaryWriter(deflate);
        writer.Write(new byte[256]); foreach (int cell in cells) writer.Write(cell);
    }
    Directory.CreateDirectory(Path.Combine(directory, "Regions"));
    using var region = new BinaryWriter(File.Create(Path.Combine(directory, "Regions", "Region 0,0.dat")));
    region.Write(0x314E4752u); region.Write(2052); region.Write((int)data.Length); region.Write(new byte[2040]);
    region.Write(0x314B4843u); region.Write(data.ToArray());
}
