using Engine;
using Engine.Graphics;
using Game;

namespace WorldRescue;

// Read-only inventory-style tiles: no live inventory, item movement or world subsystem is created.
public sealed class RecordItemWidget : CanvasWidget {
    readonly ItemStack m_item;
    readonly ClickableWidget m_click;
    public RecordItemWidget(ItemStack item) {
        m_item = item; Size = new(72); Margin = new(2);
        Children.Add(new BevelledRectangleWidget { BevelSize = -2, CenterColor = new(64, 64, 64) });
        Children.Add(new RecordBlockIconWidget(item.Value) { Margin = new(6) });
        Children.Add(new LabelWidget { Text = item.Count.ToString(), HorizontalAlignment = WidgetAlignment.Far,
            VerticalAlignment = WidgetAlignment.Far, Margin = new(6, 2), IsHitTestVisible = false });
        m_click = new ClickableWidget { SoundName = "Audio/UI/ButtonClick" }; Children.Add(m_click);
    }
    public override void Update() {
        if (m_click.IsClicked) DialogsManager.ShowDialog(null, new MessageDialog(Texts.BlockName(m_item.Value),
            Texts.Get($"数量：{m_item.Count}", $"Count: {m_item.Count}"), Texts.Get("知道了", "OK"), null, null));
    }
}

sealed class RecordBlockIconWidget : Widget {
    readonly int m_value;
    readonly PrimitivesRenderer3D m_renderer = new();
    public RecordBlockIconWidget(int value) { m_value = value; ClampToBounds = true; IsHitTestVisible = false; }
    public override void MeasureOverride(Vector2 available) { DesiredSize = new(float.PositiveInfinity); IsDrawRequired = true; }
    public override void Draw(DrawContext dc) {
        PreviewDepthBuffer.Reset(m_renderer);
        Viewport viewport = Display.Viewport;
        float scale = Math.Min(ActualSize.X, ActualSize.Y);
        Matrix projection = Matrix.CreateOrthographic(3.6f, 3.6f, -11, 9)
            * MatrixUtils.CreateScaleTranslation(scale, -scale, ActualSize.X / 2, ActualSize.Y / 2)
            * GlobalTransform * MatrixUtils.CreateScaleTranslation(2f / viewport.Width, -2f / viewport.Height, -1, 1);
        var env = new DrawBlockEnvironmentData { DrawBlockMode = DrawBlockMode.UI, Light = 15 };
        try {
            Block block = BlocksManager.Blocks[Terrain.ExtractContents(m_value)] ?? throw new InvalidOperationException();
            env.ViewProjectionMatrix = Matrix.CreateLookAt(block.GetIconViewOffset(m_value, env), Vector3.Zero, Vector3.UnitY) * projection;
            Matrix transform = Matrix.CreateTranslation(block.GetIconBlockOffset(m_value, env));
            block.DrawBlock(m_renderer, Terrain.ReplaceLight(m_value, 15), GlobalColorTransform, block.GetIconViewScale(m_value, env), ref transform, env);
            m_renderer.Flush(Matrix.Identity);
        }
        catch {
            m_renderer.Clear();
            Matrix view = Matrix.CreateLookAt(new Vector3(2, 2, 2), Vector3.Zero, Vector3.UnitY) * projection;
            m_renderer.FlatBatch(0, DepthStencilState.None).QueueBoundingBox(new BoundingBox(new Vector3(-.6f), new Vector3(.6f)), Color.Gray * GlobalColorTransform);
            m_renderer.Flush(view);
        }
    }
}
