using Engine;
using Engine.Graphics;
using Game;
using WorldRescue.Core;

namespace WorldRescue;

public sealed class NeighborhoodWidget : Widget {
    readonly PrimitivesRenderer3D m_renderer = new();
    readonly HashSet<int> m_unavailable = [];
    Vector2? m_drag;
    float m_yaw = .7f, m_pitch = .45f, m_zoom = 10f;
    public Neighborhood? Neighborhood { get; set; }
    public bool ShowSurroundings { get; set; } = true;
    public int UnavailableModels => m_unavailable.Count;

    public NeighborhoodWidget() { ClampToBounds = true; }
    public void ResetView() { m_yaw = .7f; m_pitch = .45f; m_zoom = 10f; }
    public void Zoom(float delta) => m_zoom = Math.Clamp(m_zoom + delta, 3.5f, 18f);
    public void Rotate() => m_yaw += MathF.PI / 4;
    public override void MeasureOverride(Vector2 parentAvailableSize) {
        DesiredSize = new(float.PositiveInfinity);
        IsDrawRequired = true;
    }
    public override void Update() {
        if (Input.Tap.HasValue && HitTestGlobal(Input.Tap.Value) == this) m_drag = Input.Tap;
        if (Input.Press.HasValue && m_drag.HasValue) {
            Vector2 delta = ScreenToWidget(Input.Press.Value) - ScreenToWidget(m_drag.Value);
            m_yaw -= delta.X * .01f;
            m_pitch = Math.Clamp(m_pitch + delta.Y * .01f, -.9f, 1.4f);
            m_drag = Input.Press;
        }
        if (!Input.Press.HasValue) m_drag = null;
    }

    public override void Draw(DrawContext dc) {
        if (Neighborhood == null || ActualSize.X < 1 || ActualSize.Y < 1) return;
        PreviewDepthBuffer.Reset(m_renderer);
        Vector3 eye = 12 * new Vector3(MathF.Cos(m_pitch) * MathF.Sin(m_yaw), MathF.Sin(m_pitch), MathF.Cos(m_pitch) * MathF.Cos(m_yaw));
        Viewport viewport = Display.Viewport;
        Matrix projection = Matrix.CreateLookAt(eye, Vector3.Zero, Vector3.UnitY)
            * Matrix.CreateOrthographic(m_zoom * ActualSize.X / ActualSize.Y, m_zoom, .1f, 60f)
            * MatrixUtils.CreateScaleTranslation(ActualSize.X, -ActualSize.Y, ActualSize.X / 2, ActualSize.Y / 2)
            * GlobalTransform * MatrixUtils.CreateScaleTranslation(2f / viewport.Width, -2f / viewport.Height, -1, 1);
        var env = new DrawBlockEnvironmentData { DrawBlockMode = DrawBlockMode.UI, Light = 15, ViewProjectionMatrix = projection };
        m_unavailable.Clear();
        if (ShowSurroundings) {
            foreach (PreviewCell cell in Neighborhood.Cells.Where(c => c.Position != Neighborhood.Center)
                .OrderByDescending(c => Vector3.DistanceSquared(Local(c.Position), eye))) DrawCell(cell, false, env);
        }
        PreviewCell? target = Neighborhood.Cells.FirstOrDefault(c => c.Position == Neighborhood.Center);
        if (target != null) {
            // Keep the conflict visible through the faded surroundings while retaining depth
            // testing between the target model's own faces and mesh subsets.
            PreviewDepthBuffer.Reset(m_renderer);
            DrawCell(target, true, env);
        }
        FlatBatch3D outline = m_renderer.FlatBatch(10, DepthStencilState.None);
        outline.QueueBoundingBox(new BoundingBox(new Vector3(-.53f), new Vector3(.53f)), new Color(255, 65, 65) * GlobalColorTransform);
        // Cross marks identify an empty conflict location too.
        if (target == null) {
            outline.QueueLine(new Vector3(-.4f, -.4f, 0), new Vector3(.4f, .4f, 0), Color.Red);
            outline.QueueLine(new Vector3(-.4f, .4f, 0), new Vector3(.4f, -.4f, 0), Color.Red);
        }
        m_renderer.Flush(projection);
    }

    Vector3 Local(Cell p) => new(p.X - Neighborhood!.Center.X, p.Y - Neighborhood.Center.Y, p.Z - Neighborhood.Center.Z);
    void DrawCell(PreviewCell cell, bool target, DrawBlockEnvironmentData env) {
        Vector3 position = Local(cell.Position);
        Matrix transform = Matrix.CreateTranslation(position);
        Color tint = (target ? Color.White : new Color(55, 55, 55, 55)) * GlobalColorTransform;
        try {
            Block block = BlocksManager.Blocks[Terrain.ExtractContents(cell.Value)] ?? throw new InvalidOperationException();
            // The normal registered block renderer works with base-game and third-party blocks alike.
            block.DrawBlock(m_renderer, Terrain.ReplaceLight(cell.Value, 15), tint, 1f, ref transform, env);
            // Item renderers commonly alpha-test at 0.5, which would discard the faded surroundings.
            // Override only our private batches, then restore their cache keys after flushing.
            var textures = m_renderer.TexturedBatches.Select(b => (Batch: b, AlphaTest: b.UseAlphaTest)).ToArray();
            try {
                if (!target) foreach (var entry in textures) entry.Batch.UseAlphaTest = false;
                m_renderer.Flush(Matrix.Identity);
            }
            finally {
                foreach (var entry in textures) entry.Batch.UseAlphaTest = entry.AlphaTest;
            }
        }
        catch (Exception) {
            m_renderer.Clear();
            m_unavailable.Add(cell.Value);
            m_renderer.FlatBatch(0, DepthStencilState.None).QueueBoundingBox(
                new BoundingBox(position - new Vector3(.47f), position + new Vector3(.47f)), target ? Color.Gray : tint);
            m_renderer.Flush(env.ViewProjectionMatrix!.Value);
        }
    }
}
