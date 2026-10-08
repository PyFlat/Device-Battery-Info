using System.Globalization;
using System.Text;
using DeviceBatteryInfo.Core;

namespace DeviceBatteryInfo.Ui;

// Plugins ship no images. The renderer takes absolute M/L/H/V/C/Q/A/Z (these glyphs need only
// M/L/A/Z) and fills nonzero, so a cut-out winds counter-clockwise.
internal static class DeviceGlyphs
{
    private static readonly Dictionary<BatterySourceKind, string> Paths = new()
    {
        [BatterySourceKind.System] = Laptop(),
        [BatterySourceKind.Mouse] = Mouse(),
        [BatterySourceKind.Keyboard] = Keyboard(),
        [BatterySourceKind.Headset] = Headset(),
        [BatterySourceKind.Earbuds] = Earbuds(),
        [BatterySourceKind.Phone] = Phone(),
        [BatterySourceKind.Tablet] = Tablet(),
        [BatterySourceKind.Controller] = Controller(),
        [BatterySourceKind.Pen] = Pen(),
        [BatterySourceKind.Speaker] = Speaker(),
        [BatterySourceKind.VrHeadset] = VrHeadset(),
        [BatterySourceKind.Other] = Battery(),
    };

    public static readonly string Bolt = new GlyphPath()
        .Polygon((0.6, 0.02), (0.18, 0.56), (0.46, 0.56), (0.4, 0.98), (0.82, 0.42), (0.54, 0.42))
        .ToString();

    public static string For(BatterySourceKind kind) =>
        Paths.TryGetValue(kind, out var path) ? path : Paths[BatterySourceKind.Other];

    private static string Phone() =>
        new GlyphPath()
            .RoundedRect(0.27, 0.06, 0.46, 0.88, 0.1)
            .RoundedRect(0.33, 0.13, 0.34, 0.74, 0.045, hole: true)
            .RoundedRect(0.43, 0.8, 0.14, 0.035, 0.0175)
            .ToString();

    private static string Tablet() =>
        new GlyphPath()
            .RoundedRect(0.14, 0.1, 0.72, 0.8, 0.09)
            .RoundedRect(0.2, 0.16, 0.6, 0.68, 0.04, hole: true)
            .ToString();

    private static string Laptop() =>
        new GlyphPath()
            .RoundedRect(0.17, 0.18, 0.66, 0.48, 0.06)
            .RoundedRect(0.23, 0.24, 0.54, 0.36, 0.025, hole: true)
            .RoundedRect(0.05, 0.71, 0.9, 0.08, 0.04)
            .ToString();

    private static string Mouse() =>
        new GlyphPath()
            .RoundedRect(0.28, 0.06, 0.44, 0.88, 0.22)
            .RoundedRect(0.465, 0.17, 0.07, 0.19, 0.035, hole: true)
            .ToString();

    private static string Keyboard()
    {
        var path = new GlyphPath().RoundedRect(0.04, 0.24, 0.92, 0.52, 0.08);
        for (var row = 0; row < 2; row++)
        {
            for (var key = 0; key < 5; key++)
            {
                path.RoundedRect(0.13 + (key * 0.155), 0.33 + (row * 0.12), 0.1, 0.08, 0.02, hole: true);
            }
        }

        return path.RoundedRect(0.27, 0.57, 0.46, 0.08, 0.02, hole: true).ToString();
    }

    private static string Headset() =>
        new GlyphPath()
            .Band(0.5, 0.56, 0.4, 0.31)
            .RoundedRect(0.06, 0.5, 0.22, 0.4, 0.09)
            .RoundedRect(0.72, 0.5, 0.22, 0.4, 0.09)
            .ToString();

    private static string Earbuds() =>
        new GlyphPath()
            .Circle(0.42, 0.29, 0.22)
            .RoundedRect(0.48, 0.3, 0.15, 0.64, 0.075)
            .Circle(0.34, 0.29, 0.055, hole: true)
            .ToString();

    // One outline, so the button cut-outs never sit where two solid parts overlap.
    private static string Controller() =>
        new GlyphPath()
            .MoveTo(0.3, 0.24)
            .LineTo(0.7, 0.24)
            .ArcTo(0.2, 0.9, 0.4)
            .LineTo(0.97, 0.7)
            .ArcTo(0.1, 0.78, 0.8)
            .LineTo(0.65, 0.63)
            .LineTo(0.35, 0.63)
            .LineTo(0.22, 0.8)
            .ArcTo(0.1, 0.03, 0.7)
            .LineTo(0.1, 0.4)
            .ArcTo(0.2, 0.3, 0.24)
            .Close()
            .RoundedRect(0.235, 0.33, 0.05, 0.17, 0.01, hole: true)
            .RoundedRect(0.175, 0.39, 0.06, 0.05, 0.01, hole: true)
            .RoundedRect(0.285, 0.39, 0.06, 0.05, 0.01, hole: true)
            .Circle(0.69, 0.37, 0.042, hole: true)
            .Circle(0.78, 0.46, 0.042, hole: true)
            .ToString();

    private static string Pen() =>
        new GlyphPath(rotation: 45)
            .RoundedRect(0.42, 0.04, 0.16, 0.66, 0.06)
            .Polygon((0.42, 0.72), (0.58, 0.72), (0.5, 0.96))
            .ToString();

    // The cone sits inside the grille cut-out, so it never overlaps a second solid part.
    private static string Speaker() =>
        new GlyphPath()
            .RoundedRect(0.2, 0.06, 0.6, 0.88, 0.12)
            .Circle(0.5, 0.25, 0.07, hole: true)
            .Circle(0.5, 0.62, 0.2, hole: true)
            .Circle(0.5, 0.62, 0.09)
            .ToString();

    // One outline with the nose notch; the strap only overlaps solid visor.
    private static string VrHeadset() =>
        new GlyphPath()
            .RoundedRect(0.01, 0.43, 0.98, 0.1, 0.03)
            .MoveTo(0.19, 0.27)
            .LineTo(0.81, 0.27)
            .ArcTo(0.13, 0.94, 0.4)
            .LineTo(0.94, 0.6)
            .ArcTo(0.13, 0.81, 0.73)
            .LineTo(0.63, 0.73)
            .ArcTo(0.13, 0.37, 0.73, clockwise: false)
            .LineTo(0.19, 0.73)
            .ArcTo(0.13, 0.06, 0.6)
            .LineTo(0.06, 0.4)
            .ArcTo(0.13, 0.19, 0.27)
            .Close()
            .ToString();

    private static string Battery() =>
        new GlyphPath()
            .RoundedRect(0.08, 0.3, 0.76, 0.4, 0.09)
            .RoundedRect(0.14, 0.36, 0.64, 0.28, 0.045, hole: true)
            .RoundedRect(0.19, 0.41, 0.34, 0.18, 0.025)
            .RoundedRect(0.86, 0.42, 0.06, 0.16, 0.03)
            .ToString();

    // Arcs are circular, so rotating their end points is enough.
    private sealed class GlyphPath(double rotation = 0)
    {
        private readonly StringBuilder _data = new();
        private readonly double _cos = Math.Cos(rotation * Math.PI / 180);
        private readonly double _sin = Math.Sin(rotation * Math.PI / 180);

        public GlyphPath RoundedRect(
            double x,
            double y,
            double width,
            double height,
            double radius,
            bool hole = false
        )
        {
            var r = Math.Min(radius, Math.Min(width, height) / 2);
            var right = x + width;
            var bottom = y + height;
            if (hole)
            {
                Move(x + r, y);
                Arc(r, x, y + r, clockwise: false);
                Line(x, bottom - r);
                Arc(r, x + r, bottom, clockwise: false);
                Line(right - r, bottom);
                Arc(r, right, bottom - r, clockwise: false);
                Line(right, y + r);
                Arc(r, right - r, y, clockwise: false);
            }
            else
            {
                Move(x + r, y);
                Line(right - r, y);
                Arc(r, right, y + r, clockwise: true);
                Line(right, bottom - r);
                Arc(r, right - r, bottom, clockwise: true);
                Line(x + r, bottom);
                Arc(r, x, bottom - r, clockwise: true);
                Line(x, y + r);
                Arc(r, x + r, y, clockwise: true);
            }

            return Close();
        }

        public GlyphPath Circle(double cx, double cy, double radius, bool hole = false)
        {
            Move(cx - radius, cy);
            Arc(radius, cx + radius, cy, clockwise: !hole, large: true);
            Arc(radius, cx - radius, cy, clockwise: !hole, large: true);
            return Close();
        }

        // The upper half of a ring.
        public GlyphPath Band(double cx, double cy, double outer, double inner)
        {
            Move(cx - outer, cy);
            Arc(outer, cx + outer, cy, clockwise: true);
            Line(cx + inner, cy);
            Arc(inner, cx - inner, cy, clockwise: false);
            return Close();
        }

        public GlyphPath Polygon(params (double X, double Y)[] points)
        {
            Move(points[0].X, points[0].Y);
            foreach (var (x, y) in points.Skip(1))
            {
                Line(x, y);
            }

            return Close();
        }

        public GlyphPath MoveTo(double x, double y)
        {
            Move(x, y);
            return this;
        }

        public GlyphPath LineTo(double x, double y)
        {
            Line(x, y);
            return this;
        }

        public GlyphPath ArcTo(double radius, double x, double y, bool clockwise = true)
        {
            Arc(radius, x, y, clockwise);
            return this;
        }

        public override string ToString() => _data.ToString().Trim();

        private void Move(double x, double y) => Append("M", x, y);

        private void Line(double x, double y) => Append("L", x, y);

        private void Arc(double radius, double x, double y, bool clockwise, bool large = false)
        {
            var (px, py) = Rotate(x, y);
            _data
                .Append("A ")
                .Append(Number(radius))
                .Append(' ')
                .Append(Number(radius))
                .Append(" 0 ")
                .Append(large ? '1' : '0')
                .Append(' ')
                .Append(clockwise ? '1' : '0')
                .Append(' ')
                .Append(Number(px))
                .Append(' ')
                .Append(Number(py))
                .Append(' ');
        }

        public GlyphPath Close()
        {
            _data.Append("Z ");
            return this;
        }

        private void Append(string command, double x, double y)
        {
            var (px, py) = Rotate(x, y);
            _data
                .Append(command)
                .Append(' ')
                .Append(Number(px))
                .Append(' ')
                .Append(Number(py))
                .Append(' ');
        }

        private (double X, double Y) Rotate(double x, double y)
        {
            var dx = x - 0.5;
            var dy = y - 0.5;
            return (0.5 + (dx * _cos) - (dy * _sin), 0.5 + (dx * _sin) + (dy * _cos));
        }

        private static string Number(double value) =>
            Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);
    }
}
