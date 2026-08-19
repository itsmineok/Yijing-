using System.Windows;
using System.Windows.Media;

namespace Yijing.Desktop.Controls;

public static class BoardRenderPalette
{
    public static readonly Brush Wood = Frozen(new SolidColorBrush(Color.FromRgb(0xD5, 0xA4, 0x5C)));
    public static readonly Brush Grid = Frozen(new SolidColorBrush(Color.FromRgb(0x42, 0x2D, 0x19)));
    public static readonly Brush Coordinate = Frozen(new SolidColorBrush(Color.FromRgb(0x5A, 0x3E, 0x23)));
    public static readonly Brush Candidate = Frozen(new SolidColorBrush(Color.FromArgb(0xCC, 0x16, 0x91, 0x7A)));
    public static readonly Brush CandidateText = Frozen(new SolidColorBrush(Colors.White));
    public static readonly Brush BlackStone = Frozen(CreateRadial(Color.FromRgb(0x4B, 0x52, 0x50), Color.FromRgb(0x08, 0x0B, 0x0A)));
    public static readonly Brush WhiteStone = Frozen(CreateRadial(Colors.White, Color.FromRgb(0xC5, 0xCE, 0xCA)));
    public static readonly Brush BlackLastMove = Frozen(new SolidColorBrush(Color.FromRgb(0xE9, 0xF0, 0xED)));
    public static readonly Brush WhiteLastMove = Frozen(new SolidColorBrush(Color.FromRgb(0x10, 0x18, 0x17)));
    public static readonly Pen StoneOutlinePen = Frozen(new Pen(
        new SolidColorBrush(Color.FromArgb(0x38, 0, 0, 0)), 0.8));
    public static readonly Pen BlackLastMovePen = Frozen(new Pen(BlackLastMove, 1.5));
    public static readonly Pen WhiteLastMovePen = Frozen(new Pen(WhiteLastMove, 1.5));

    private static RadialGradientBrush CreateRadial(Color highlight, Color shadow) => new()
    {
        Center = new System.Windows.Point(0.34, 0.30),
        GradientOrigin = new System.Windows.Point(0.28, 0.24),
        RadiusX = 0.72,
        RadiusY = 0.72,
        GradientStops =
        {
            new GradientStop(highlight, 0),
            new GradientStop(shadow, 1)
        }
    };

    private static T Frozen<T>(T value) where T : Freezable
    {
        value.Freeze();
        return value;
    }
}
