using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Yijing.Application.Analysis;
using Yijing.Domain.Board;

namespace Yijing.Desktop.Controls;

public sealed class GoBoardControl : FrameworkElement
{
    private const double BoardPadding = 28;
    private const string Columns = "ABCDEFGHJKLMNOPQRST";

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(BoardState), typeof(GoBoardControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CandidatesProperty = DependencyProperty.Register(
        nameof(Candidates), typeof(IEnumerable), typeof(GoBoardControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LastMoveProperty = DependencyProperty.Register(
        nameof(LastMove), typeof(BoardPoint?), typeof(GoBoardControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DeadStonesProperty = DependencyProperty.Register(
        nameof(DeadStones), typeof(IEnumerable), typeof(GoBoardControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PlayCommandProperty = DependencyProperty.Register(
        nameof(PlayCommand), typeof(ICommand), typeof(GoBoardControl));

    public static readonly DependencyProperty IsInputEnabledProperty = DependencyProperty.Register(
        nameof(IsInputEnabled), typeof(bool), typeof(GoBoardControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowCoordinatesProperty = DependencyProperty.Register(
        nameof(ShowCoordinates), typeof(bool), typeof(GoBoardControl),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public GoBoardControl()
    {
        SnapsToDevicePixels = true;
        Focusable = true;
        DataContextChanged += OnDataContextChanged;
    }

    public BoardState? State
    {
        get => (BoardState?)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public IEnumerable? Candidates
    {
        get => (IEnumerable?)GetValue(CandidatesProperty);
        set => SetValue(CandidatesProperty, value);
    }

    public BoardPoint? LastMove
    {
        get => (BoardPoint?)GetValue(LastMoveProperty);
        set => SetValue(LastMoveProperty, value);
    }

    public IEnumerable? DeadStones
    {
        get => (IEnumerable?)GetValue(DeadStonesProperty);
        set => SetValue(DeadStonesProperty, value);
    }

    public ICommand? PlayCommand
    {
        get => (ICommand?)GetValue(PlayCommandProperty);
        set => SetValue(PlayCommandProperty, value);
    }

    public bool IsInputEnabled
    {
        get => (bool)GetValue(IsInputEnabledProperty);
        set => SetValue(IsInputEnabledProperty, value);
    }

    public bool ShowCoordinates
    {
        get => (bool)GetValue(ShowCoordinatesProperty);
        set => SetValue(ShowCoordinatesProperty, value);
    }

    public static Point PointToPixel(BoardPoint point, int size, Rect bounds)
    {
        ValidateGeometry(point, size, bounds);
        var spacingX = bounds.Width / (size - 1);
        var spacingY = bounds.Height / (size - 1);
        return new Point(bounds.Left + (point.Column * spacingX), bounds.Top + (point.Row * spacingY));
    }

    public static BoardPoint? PixelToPoint(Point pixel, int size, Rect bounds, double tolerance)
    {
        if (size < 2) throw new ArgumentOutOfRangeException(nameof(size));
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(bounds));
        if (!double.IsFinite(tolerance) || tolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(tolerance));

        var spacingX = bounds.Width / (size - 1);
        var spacingY = bounds.Height / (size - 1);
        var column = (int)Math.Round((pixel.X - bounds.Left) / spacingX, MidpointRounding.AwayFromZero);
        var row = (int)Math.Round((pixel.Y - bounds.Top) / spacingY, MidpointRounding.AwayFromZero);
        var point = new BoardPoint(row, column);
        if (!point.IsInside(size)) return null;

        var target = PointToPixel(point, size, bounds);
        var distance = Math.Sqrt(Math.Pow(pixel.X - target.X, 2) + Math.Pow(pixel.Y - target.Y, 2));
        return distance <= tolerance ? point : null;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var state = State ?? BoardState.Empty(19);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        var boardRect = GetBoardRect();
        var gridBounds = Deflate(boardRect, BoardPadding);
        drawingContext.DrawRoundedRectangle(BoardRenderPalette.Wood, null, boardRect, 3, 3);
        DrawGrid(drawingContext, state.Size, gridBounds);
        DrawStarPoints(drawingContext, state.Size, gridBounds);
        if (ShowCoordinates) DrawCoordinates(drawingContext, state.Size, gridBounds);
        DrawStones(drawingContext, state, gridBounds);
        DrawDeadStones(drawingContext, state.Size, gridBounds);
        DrawCandidates(drawingContext, state.Size, gridBounds);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!IsInputEnabled || State is not { } state || PlayCommand is not { } command) return;

        var bounds = Deflate(GetBoardRect(), BoardPadding);
        var spacing = Math.Min(bounds.Width, bounds.Height) / (state.Size - 1);
        var point = PixelToPoint(e.GetPosition(this), state.Size, bounds, spacing * 0.45);
        if (point is { } move && command.CanExecute(move)) command.Execute(move);
    }

    private void DrawGrid(DrawingContext context, int size, Rect bounds)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var thickness = 1 / Math.Max(dpi.DpiScaleX, dpi.DpiScaleY);
        var pen = new Pen(BoardRenderPalette.Grid, thickness);
        pen.Freeze();

        for (var index = 0; index < size; index++)
        {
            var vertical = PointToPixel(new BoardPoint(0, index), size, bounds).X;
            var horizontal = PointToPixel(new BoardPoint(index, 0), size, bounds).Y;
            vertical = AlignStroke(vertical, dpi.DpiScaleX);
            horizontal = AlignStroke(horizontal, dpi.DpiScaleY);
            context.DrawLine(pen, new Point(vertical, bounds.Top), new Point(vertical, bounds.Bottom));
            context.DrawLine(pen, new Point(bounds.Left, horizontal), new Point(bounds.Right, horizontal));
        }
    }

    private static void DrawStarPoints(DrawingContext context, int size, Rect bounds)
    {
        IEnumerable<BoardPoint> stars = size switch
        {
            19 => from row in new[] { 3, 9, 15 } from column in new[] { 3, 9, 15 } select new BoardPoint(row, column),
            13 => [new(3, 3), new(3, 9), new(6, 6), new(9, 3), new(9, 9)],
            9 => [new(2, 2), new(2, 6), new(4, 4), new(6, 2), new(6, 6)],
            _ => []
        };
        foreach (var star in stars)
            context.DrawEllipse(BoardRenderPalette.Grid, null, PointToPixel(star, size, bounds), 3.2, 3.2);
    }

    private void DrawCoordinates(DrawingContext context, int size, Rect bounds)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var typeface = new Typeface("Segoe UI");
        for (var index = 0; index < size; index++)
        {
            var column = CreateText(Columns[index].ToString(), typeface, 11, dpi.PixelsPerDip);
            var x = PointToPixel(new BoardPoint(0, index), size, bounds).X - (column.Width / 2);
            context.DrawText(column, new Point(x, bounds.Top - 20));
            context.DrawText(column, new Point(x, bounds.Bottom + 6));

            var number = CreateText((size - index).ToString(CultureInfo.InvariantCulture), typeface, 11, dpi.PixelsPerDip);
            var y = PointToPixel(new BoardPoint(index, 0), size, bounds).Y - (number.Height / 2);
            context.DrawText(number, new Point(bounds.Left - 8 - number.Width, y));
            context.DrawText(number, new Point(bounds.Right + 8, y));
        }
    }

    private void DrawStones(DrawingContext context, BoardState state, Rect bounds)
    {
        var spacing = Math.Min(bounds.Width, bounds.Height) / (state.Size - 1);
        var radius = Math.Max(2, (spacing * 0.48) - 0.5);
        for (var row = 0; row < state.Size; row++)
        for (var column = 0; column < state.Size; column++)
        {
            var point = new BoardPoint(row, column);
            var color = state.At(point);
            if (color == StoneColor.Empty) continue;
            var center = PointToPixel(point, state.Size, bounds);
            context.DrawEllipse(color == StoneColor.Black ? BoardRenderPalette.BlackStone : BoardRenderPalette.WhiteStone,
                BoardRenderPalette.StoneOutlinePen, center, radius, radius);
            if (LastMove == point)
            {
                var ring = color == StoneColor.Black
                    ? BoardRenderPalette.BlackLastMovePen
                    : BoardRenderPalette.WhiteLastMovePen;
                context.DrawEllipse(null, ring, center, 3, 3);
            }
        }
    }

    private void DrawCandidates(DrawingContext context, int size, Rect bounds)
    {
        if (Candidates is null) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var spacing = Math.Min(bounds.Width, bounds.Height) / (size - 1);
        var radius = Math.Clamp(spacing * 0.32, 8, 15);
        var number = 0;
        foreach (var item in Candidates)
        {
            if (number == 3) break;
            if (item is not CandidateMove candidate || !TryParseMove(candidate.Move, size, out var point)) continue;
            number++;
            var center = PointToPixel(point, size, bounds);
            context.DrawEllipse(BoardRenderPalette.Candidate, null, center, radius, radius);
            var text = new FormattedText(number.ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, typeface, 11, BoardRenderPalette.CandidateText, dpi.PixelsPerDip);
            context.DrawText(text, new Point(center.X - (text.Width / 2), center.Y - (text.Height / 2)));
        }
    }

    private void DrawDeadStones(DrawingContext context, int size, Rect bounds)
    {
        if (DeadStones is null) return;
        var spacing = Math.Min(bounds.Width, bounds.Height) / (size - 1);
        var radius = Math.Clamp(spacing * .22, 4, 10);
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(216, 95, 95)), 2.5);
        pen.Freeze();
        foreach (var item in DeadStones)
        {
            if (item is not BoardPoint point || !point.IsInside(size)) continue;
            var center = PointToPixel(point, size, bounds);
            context.DrawLine(pen, new Point(center.X - radius, center.Y - radius),
                new Point(center.X + radius, center.Y + radius));
            context.DrawLine(pen, new Point(center.X + radius, center.Y - radius),
                new Point(center.X - radius, center.Y + radius));
        }
    }

    private static bool TryParseMove(string move, int size, out BoardPoint point)
    {
        point = default;
        if (string.Equals(move, "pass", StringComparison.OrdinalIgnoreCase) || move.Length < 2) return false;
        var column = Columns.IndexOf(char.ToUpperInvariant(move[0]));
        if (column < 0 || !int.TryParse(move[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var number)) return false;
        point = new BoardPoint(size - number, column);
        return point.IsInside(size);
    }

    private Rect GetBoardRect()
    {
        var side = Math.Max(0, Math.Min(ActualWidth, ActualHeight));
        return new Rect((ActualWidth - side) / 2, (ActualHeight - side) / 2, side, side);
    }

    private static Rect Deflate(Rect rect, double amount) =>
        new(rect.Left + amount, rect.Top + amount, Math.Max(1, rect.Width - (2 * amount)), Math.Max(1, rect.Height - (2 * amount)));

    private static double AlignStroke(double value, double dpiScale) =>
        (Math.Round(value * dpiScale) + 0.5) / dpiScale;

    private static FormattedText CreateText(string value, Typeface typeface, double size, double pixelsPerDip) =>
        new(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size,
            BoardRenderPalette.Coordinate, pixelsPerDip);

    private static void ValidateGeometry(BoardPoint point, int size, Rect bounds)
    {
        if (size < 2) throw new ArgumentOutOfRangeException(nameof(size));
        if (!point.IsInside(size)) throw new ArgumentOutOfRangeException(nameof(point));
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(bounds));
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldValue) oldValue.PropertyChanged -= OnViewModelPropertyChanged;
        if (e.NewValue is INotifyPropertyChanged newValue) newValue.PropertyChanged += OnViewModelPropertyChanged;
        InvalidateVisual();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(State) or nameof(Candidates) or nameof(LastMove)) InvalidateVisual();
    }
}
