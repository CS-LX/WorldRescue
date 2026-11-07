using Engine;
using Engine.Media;
using Game;

namespace WorldRescue;

// A new, mod-local control. Host and other mods' controls remain untouched.
public sealed class RescueIconButtonWidget : ButtonWidget {
    readonly RescueIconWidget m_icon;
    readonly LabelWidget m_label;
    readonly RectangleWidget m_selection;
    public ClickableWidget Clickable { get; }
    public Subtexture? Subtexture { get => m_icon.Subtexture; set => m_icon.Subtexture = value; }
    public float IconSize { get => m_icon.Size.X; set => m_icon.Size = new(value); }
    public override bool IsClicked => IsEnabledGlobal && Clickable.IsClicked;
    public override bool IsChecked { get => Clickable.IsChecked; set => Clickable.IsChecked = value; }
    public override bool IsAutoCheckingEnabled { get => Clickable.IsAutoCheckingEnabled; set => Clickable.IsAutoCheckingEnabled = value; }
    public override string Text { get => m_label.Text; set => m_label.Text = value; }
    public override BitmapFont Font { get => m_label.Font; set => m_label.Font = value; }
    public override Color Color { get; set; } = Color.White;

    public RescueIconButtonWidget() {
        Size = new(60, 52);
        m_icon = new RescueIconWidget { HorizontalAlignment = WidgetAlignment.Center, VerticalAlignment = WidgetAlignment.Center };
        m_label = new LabelWidget { HorizontalAlignment = WidgetAlignment.Far, VerticalAlignment = WidgetAlignment.Far,
            Margin = new(12, 10), IsHitTestVisible = false };
        m_selection = new RectangleWidget { Size = new(40, 2), FillColor = Color.White, OutlineColor = Color.Transparent,
            HorizontalAlignment = WidgetAlignment.Center, VerticalAlignment = WidgetAlignment.Far, Margin = new(0, 4) };
        Clickable = new ClickableWidget { SoundName = "Audio/UI/ButtonClick" };
        Children.Add(m_icon); Children.Add(m_label); Children.Add(m_selection); Children.Add(Clickable);
    }
    public override void MeasureOverride(Vector2 available) {
        Color tint = !IsEnabledGlobal ? new Color(150, 150, 150)
            : Clickable.IsPressed ? new Color(190, 220, 255) : Color;
        m_icon.Color = tint; m_label.Color = tint;
        m_icon.RenderTransform = Clickable.IsPressed
            ? Matrix.CreateTranslation(new Vector3(0, 1, 0)) : Matrix.Identity;
        m_selection.IsVisible = IsChecked;
        base.MeasureOverride(available);
    }
}
