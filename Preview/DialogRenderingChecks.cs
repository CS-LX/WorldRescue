using Engine;
using Engine.Graphics;
using Engine.Media;
using Game;
using WorldRescue;
using WorldRescue.Core;

static class DialogRenderingChecks {
    public static void Verify(string directory, byte[] project, string output) {
        LanguageControl.CurrentLanguageName = "zh-CN";
        var failures = new List<string>();
        foreach (var size in new[] { (1024, 768, 1f), (1920, 1080, 1.6941177f) }) {
            int width = size.Item1, height = size.Item2;
            using var target = new RenderTarget2D(width, height, 1, ColorFormat.Rgba8888, DepthFormat.Depth24Stencil8);
            Display.RenderTarget = target;
            Display.Viewport = new(0, 0, width, height);
            Display.ScissorRectangle = new(0, 0, width, height);
            var root = new CanvasWidget { WidgetsHierarchyInput = new WidgetInput(), LayoutTransform = Matrix.CreateScale(size.Item3, size.Item3, 1) };
            var dialog = new RescueDialog(directory, new RepairPlan(project), () => { }, () => { });
            DialogsManager.ShowDialog(root, dialog);
            var record = dialog.Children.Find<RescueIconButtonWidget>("Record1");
            record.Clickable.IsClicked = true;
            dialog.Update();
            record.Clickable.IsClicked = false;
            for (int i = 0; i < 200; i++) {
                dialog.Update();
                if (dialog.Children.Find<NeighborhoodWidget>("Preview").Neighborhood?.Cells.Count > 0) break;
                Thread.Sleep(5);
            }
            void Layout() => Widget.LayoutWidgetsHierarchy(root, new(width / size.Item3, height / size.Item3));
            void Draw(bool clearDepth) {
                // The host loading screen does not clear depth on every frame.
                Display.Clear(new Color(18, 22, 28), clearDepth ? 1f : null, null);
                Layout();
                Widget.DrawWidgetsHierarchy(root);
            }
            Color[] Pixels() {
                var pixels = new Color[width * height];
                target.GetData(pixels, 0, new(0, 0, width, height));
                return pixels;
            }
            Layout();
            for (int i = 0; i <= 12; i++) {
                DialogsManager.m_animationData[dialog].Factor = i / 12f;
                DialogsManager.UpdateDialog(dialog, DialogsManager.m_animationData[dialog]);
                Draw(i == 0);
            }
            var history = Pixels();
            RenderTarget2D.Save(target, Path.Combine(output, $"animation-history-{width}.png"), ImageFileFormat.Png, true);
            Draw(true);
            var clean = Pixels();
            RenderTarget2D.Save(target, Path.Combine(output, $"animation-clean-{width}.png"), ImageFileFormat.Png, true);
            int different = history.Zip(clean).Count(p => p.First != p.Second);
            if (different != 0) failures.Add($"{width}: {different} pixels depend on previous animation frames.");
            Vector2 center = (dialog.GlobalBounds.Min + dialog.GlobalBounds.Max) * .5f;
            if (Vector2.Distance(center, new(width / 2f, height / 2f)) > 1f || Vector2.Distance(dialog.ActualSize, dialog.Size) > .1f)
                failures.Add($"{width}: dialog is not centered at its measured size ({dialog.ActualSize} / {dialog.Size}, center {center}).");
            // Move the inventory under its real scroll clipping, then switch records and rotate the scene.
            dialog.Children.Find<ScrollPanelWidget>("RecordsScroll").ScrollPosition = 28;
            Draw(false);
            dialog.Children.Find<ScrollPanelWidget>("RecordsScroll").ScrollPosition = 0;
            dialog.Children.Find<NeighborhoodWidget>("Preview").Rotate();
            foreach (string name in new[] { "Record0", "Record1" }) {
                var button = dialog.Children.Find<RescueIconButtonWidget>(name);
                button.Clickable.IsClicked = true; dialog.Update(); button.Clickable.IsClicked = false;
                Draw(false);
            }
            history = Pixels();
            RenderTarget2D.Save(target, Path.Combine(output, $"interaction-history-{width}.png"), ImageFileFormat.Png, true);
            Draw(true);
            clean = Pixels();
            RenderTarget2D.Save(target, Path.Combine(output, $"interaction-clean-{width}.png"), ImageFileFormat.Png, true);
            var changed = Enumerable.Range(0, history.Length).Where(i => history[i] != clean[i]).ToArray();
            different = changed.Length;
            if (different > 0) Console.WriteLine($"Changed bounds: {changed.Min(i => i % width)},{changed.Min(i => i / width)} .. {changed.Max(i => i % width)},{changed.Max(i => i / width)}");
            if (different != 0) failures.Add($"{width}: {different} pixels depend on previous scroll/record/rotation frames.");
            Console.WriteLine($"Dialog rendering {width}x{height}: actual={dialog.ActualSize}, desired={dialog.Size}, center={center}; animation and scroll/record/rotation compared with fresh depth.");
            DialogsManager.m_dialogs.Remove(dialog);
            DialogsManager.m_animationData.Remove(dialog);
            root.Dispose();
        }
        VerifyDepthIsolation();
        Display.RenderTarget = null;
        if (failures.Count > 0) throw new Exception(string.Join(Environment.NewLine, failures));
    }

    static void VerifyDepthIsolation() {
        using var target = new RenderTarget2D(256, 160, 1, ColorFormat.Rgba8888, DepthFormat.Depth24Stencil8);
        Display.RenderTarget = target; Display.Viewport = new(0, 0, 256, 160); Display.ScissorRectangle = new(0, 0, 256, 160);
        Display.Clear(Color.Black, .1f, 0);
        var root = new CanvasWidget();
        var tile = new RecordItemWidget(new(5, 20));
        root.Children.Add(tile); root.SetWidgetPosition(tile, new(80, 40));
        Widget.LayoutWidgetsHierarchy(root, new(256, 160)); Widget.DrawWidgetsHierarchy(root);
        var before = new Color[256 * 160]; target.GetData(before, 0, new(0, 0, 256, 160));
        var renderer = new PrimitivesRenderer3D();
        renderer.FlatBatch().QueueQuad(new(-1, -1, .8f), new(1, -1, .8f), new(1, 1, .8f), new(-1, 1, .8f), Color.Green);
        renderer.Flush(Matrix.Identity);
        var after = new Color[256 * 160]; target.GetData(after, 0, new(0, 0, 256, 160));
        int insideChanges = 0;
        for (int y = 0; y < 160; y++) for (int x = 0; x < 256; x++) {
            if (before[x + y * 256] == after[x + y * 256]) continue;
            if (x < tile.GlobalBounds.Min.X || x >= tile.GlobalBounds.Max.X || y < tile.GlobalBounds.Min.Y || y >= tile.GlobalBounds.Max.Y)
                throw new Exception("Preview modified depth outside its widget.");
            insideChanges++;
        }
        if (insideChanges == 0) throw new Exception("The depth-isolation probe did not exercise a reset.");
        Console.WriteLine("GPU depth readback: preview resets are confined to their clipped widget; outside depth remains unchanged.");
    }
}
