using Engine;
using Engine.Graphics;

namespace WorldRescue;

internal static class PreviewDepthBuffer {
    static readonly DepthStencilState m_resetDepth = new() {
        DepthBufferTestEnable = true, DepthBufferWriteEnable = true, DepthBufferFunction = CompareFunction.Always
    };
    static readonly BlendState m_preserveColor = new() {
        ColorSourceBlend = Blend.Zero, ColorDestinationBlend = Blend.One,
        AlphaSourceBlend = Blend.Zero, AlphaDestinationBlend = Blend.One
    };

    public static void Reset(PrimitivesRenderer3D renderer) {
        // Loading screens retain depth between frames. Reset only this widget's visible quad,
        // respecting scroll clipping. Display.Clear would ignore scissoring and clear the whole screen.
        // Both callers use ClampToBounds: cover the complete pixel scissor, including its rounded
        // edge pixels, rather than a smaller floating-point quad that leaves stale depth at the edges.
        renderer.FlatBatch(int.MinValue, m_resetDepth, RasterizerState.CullNoneScissor, m_preserveColor).QueueQuad(
            new(-1, -1, 1), new(1, -1, 1), new(1, 1, 1), new(-1, 1, 1), Color.Transparent);
        renderer.Flush(Matrix.Identity);
    }
}
