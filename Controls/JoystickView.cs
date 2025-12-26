using Microsoft.Maui.Graphics;

namespace RacerRemote.Controls;

public sealed class JoystickView : ContentView
{
    public static readonly BindableProperty StickXProperty = BindableProperty.Create(
        nameof(StickX), typeof(double), typeof(JoystickView), 0d, BindingMode.TwoWay,
        coerceValue: (_, v) => Math.Clamp((double)v, -1d, 1d));

    public static readonly BindableProperty StickYProperty = BindableProperty.Create(
        nameof(StickY), typeof(double), typeof(JoystickView), 0d, BindingMode.TwoWay,
        coerceValue: (_, v) => Math.Clamp((double)v, -1d, 1d));

    public static readonly BindableProperty DeadZoneProperty = BindableProperty.Create(
        nameof(DeadZone), typeof(double), typeof(JoystickView), 0.07d,
        coerceValue: (_, v) => Math.Clamp((double)v, 0d, 0.5d));

    public static readonly BindableProperty QuantizeTo12BitProperty = BindableProperty.Create(
        nameof(QuantizeTo12Bit),
        typeof(bool),
        typeof(JoystickView),
        true);

    public double StickX
    {
        get => (double)GetValue(StickXProperty);
        set => SetValue(StickXProperty, value);
    }

    public double StickY
    {
        get => (double)GetValue(StickYProperty);
        set => SetValue(StickYProperty, value);
    }

    public double DeadZone
    {
        get => (double)GetValue(DeadZoneProperty);
        set => SetValue(DeadZoneProperty, value);
    }

    public bool QuantizeTo12Bit
    {
        get => (bool)GetValue(QuantizeTo12BitProperty);
        set => SetValue(QuantizeTo12BitProperty, value);
    }

    private readonly GraphicsView _graphicsView;
    private readonly JoystickDrawable _drawable;

    public JoystickView()
    {
        _drawable = new JoystickDrawable();
        _graphicsView = new GraphicsView
        {
            Drawable = _drawable,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };

        _graphicsView.StartInteraction += (_, e) => UpdateFromPoint(e.Touches?.FirstOrDefault());
        _graphicsView.DragInteraction += (_, e) => UpdateFromPoint(e.Touches?.FirstOrDefault());
        _graphicsView.EndInteraction += (_, __) => ResetStick();
        _graphicsView.CancelInteraction += (_, __) => ResetStick();

        Content = _graphicsView;

        ResetStick();
    }

    protected override void OnParentSet()
    {
        base.OnParentSet();

        if (Parent is null)
        {
            ResetStick();
        }
    }

    private void ResetStick()
    {
        StickX = 0;
        StickY = 0;
        _drawable.SetNormalized(0, 0);
        _graphicsView.Invalidate();
    }

    private void UpdateFromPoint(PointF? touch)
    {
        if (touch is null)
        {
            return;
        }

        var size = _graphicsView.Bounds;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        var cx = (float)(size.Width / 2.0);
        var cy = (float)(size.Height / 2.0);

        var dx = touch.Value.X - cx;
        var dy = touch.Value.Y - cy;

        var radius = (float)(Math.Min(size.Width, size.Height) / 2.0);
        radius *= 0.92f;

        var len = MathF.Sqrt(dx * dx + dy * dy);
        if (len > radius && len > 0)
        {
            var scale = radius / len;
            dx *= scale;
            dy *= scale;
        }

        var nx = radius <= 0 ? 0 : (dx / radius);
        var ny = radius <= 0 ? 0 : (-dy / radius);

        nx = ApplyDeadZone(nx);
        ny = ApplyDeadZone(ny);

        if (QuantizeTo12Bit)
        {
            nx = QuantizeNormalizedTo12Bit(nx);
            ny = QuantizeNormalizedTo12Bit(ny);
        }

        StickX = nx;
        StickY = ny;

        _drawable.SetNormalized((float)nx, (float)ny);
        _graphicsView.Invalidate();
    }

    private static float QuantizeNormalizedTo12Bit(float v)
    {
        v = Math.Clamp(v, -1f, 1f);

        // Map [-1..+1] -> [0..4095]
        var counts = (int)MathF.Round(((v + 1f) * 0.5f) * 4095f);
        counts = Math.Clamp(counts, 0, 4095);

        // Map back [0..4095] -> [-1..+1]
        var quantized = (counts / 4095f) * 2f - 1f;
        return quantized;
    }

    private float ApplyDeadZone(float value)
    {
        var abs = Math.Abs(value);
        if (abs < (float)DeadZone)
        {
            return 0;
        }

        var sign = Math.Sign(value);
        var dz = (float)DeadZone;
        var scaled = (abs - dz) / (1.0f - dz);
        return sign * scaled;
    }

    private sealed class JoystickDrawable : IDrawable
    {
        private float _x;
        private float _y;

        public void SetNormalized(float x, float y)
        {
            _x = Math.Clamp(x, -1f, 1f);
            _y = Math.Clamp(y, -1f, 1f);
        }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var cx = dirtyRect.Center.X;
            var cy = dirtyRect.Center.Y;

            // Outer bezel / ring
            var outerRadius = Math.Min(dirtyRect.Width, dirtyRect.Height) * 0.48f;
            var innerRadius = outerRadius * 0.82f;

            canvas.SaveState();

            canvas.FillColor = Colors.Transparent;
            canvas.FillRectangle(dirtyRect);

            // Bezel gradient (raised)
            canvas.SetFillPaint(
                new LinearGradientPaint(
                    new[]
                    {
                        new PaintGradientStop(0f, Color.FromArgb("#FFF7F1")),
                        new PaintGradientStop(1f, Color.FromArgb("#CDB8AA"))
                    },
                    new PointF(cx - outerRadius, cy - outerRadius),
                    new PointF(cx + outerRadius, cy + outerRadius)),
                dirtyRect);
            canvas.FillCircle(cx, cy, outerRadius);

            // Outer rim stroke
            canvas.StrokeSize = Math.Max(1, outerRadius * 0.03f);
            canvas.StrokeColor = Color.FromArgb("#7A6A60");
            canvas.DrawCircle(cx, cy, outerRadius);

            // Inner well (recessed)
            canvas.SetFillPaint(
                new LinearGradientPaint(
                    new[]
                    {
                        new PaintGradientStop(0f, Color.FromArgb("#2A2A2A")),
                        new PaintGradientStop(1f, Color.FromArgb("#141414"))
                    },
                    new PointF(cx, cy - innerRadius),
                    new PointF(cx, cy + innerRadius)),
                dirtyRect);
            canvas.FillCircle(cx, cy, innerRadius);

            // Inner highlight + shadow strokes (gives recessed feel)
            canvas.StrokeSize = Math.Max(1, outerRadius * 0.01f);
            canvas.StrokeColor = Color.FromArgb("#60FFFFFF");
            canvas.DrawCircle(cx, cy, innerRadius);

            canvas.StrokeColor = Color.FromArgb("#60000000");
            canvas.DrawCircle(cx, cy, innerRadius * 0.985f);

            // Crosshair (engraved)
            var cross = innerRadius * 0.85f;
            canvas.StrokeSize = Math.Max(1, outerRadius * 0.008f);
            canvas.StrokeColor = Color.FromArgb("#3FFFFFFF");
            canvas.DrawLine(cx - cross, cy, cx + cross, cy);
            canvas.DrawLine(cx, cy - cross, cx, cy + cross);

            canvas.StrokeColor = Color.FromArgb("#50000000");
            canvas.DrawLine(cx - cross, cy + 1, cx + cross, cy + 1);
            canvas.DrawLine(cx + 1, cy - cross, cx + 1, cy + cross);

            // Knob
            var knobRadius = innerRadius * 0.33f;
            var knobX = cx + (_x * innerRadius);
            var knobY = cy - (_y * innerRadius);

            // Knob shadow
            canvas.FillColor = Color.FromArgb("#66000000");
            canvas.FillCircle(knobX + knobRadius * 0.08f, knobY + knobRadius * 0.12f, knobRadius * 1.02f);

            // Knob body
            canvas.SetFillPaint(
                new LinearGradientPaint(
                    new[]
                    {
                        new PaintGradientStop(0f, Color.FromArgb("#FFFFFF")),
                        new PaintGradientStop(0.55f, Color.FromArgb("#E6E6E6")),
                        new PaintGradientStop(1f, Color.FromArgb("#B8B8B8"))
                    },
                    new PointF(knobX - knobRadius, knobY - knobRadius),
                    new PointF(knobX + knobRadius, knobY + knobRadius)),
                dirtyRect);
            canvas.FillCircle(knobX, knobY, knobRadius);

            // Knob rim
            canvas.StrokeSize = Math.Max(1, knobRadius * 0.10f);
            canvas.StrokeColor = Color.FromArgb("#7F6F66");
            canvas.DrawCircle(knobX, knobY, knobRadius);

            // Gloss highlight
            canvas.SetFillPaint(
                new RadialGradientPaint(
                    new[]
                    {
                        new PaintGradientStop(0f, Color.FromArgb("#A0FFFFFF")),
                        new PaintGradientStop(1f, Color.FromArgb("#00FFFFFF"))
                    },
                    new PointF(knobX - knobRadius * 0.25f, knobY - knobRadius * 0.35f),
                    knobRadius),
                dirtyRect);
            canvas.FillCircle(knobX - knobRadius * 0.15f, knobY - knobRadius * 0.20f, knobRadius * 0.85f);

            canvas.RestoreState();
        }
    }
}
