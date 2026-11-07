using Engine;
using Engine.Graphics;
using Game;

namespace WorldRescue;

// Straight-alpha artwork needs matching blending and continuous alpha (not an alpha-test cutoff).
public sealed class RescueIconWidget : Widget {
    public Subtexture? Subtexture { get; set; }
    public Vector2 Size { get; set; } = new(32);
    public Color Color { get; set; } = Color.White;
    public RescueIconWidget() => IsHitTestVisible = false;
    public override void MeasureOverride(Vector2 available) { DesiredSize = Size; IsDrawRequired = true; }
    public override void Draw(DrawContext dc) {
        if (Subtexture == null) return;
        Vector2 uv0 = Subtexture.TopLeft, uv1 = Subtexture.BottomRight;
        dc.PrimitivesRenderer2D.TexturedBatch(Subtexture.Texture, false, 0, DepthStencilState.None, null,
            BlendState.NonPremultiplied, SamplerState.LinearClamp).QueueQuad(
                Vector2.Transform(Vector2.Zero, GlobalTransform),
                Vector2.Transform(new Vector2(ActualSize.X, 0), GlobalTransform),
                Vector2.Transform(ActualSize, GlobalTransform),
                Vector2.Transform(new Vector2(0, ActualSize.Y), GlobalTransform), 0,
                uv0, new Vector2(uv1.X, uv0.Y), uv1, new Vector2(uv0.X, uv1.Y), Color * GlobalColorTransform);
    }
}
