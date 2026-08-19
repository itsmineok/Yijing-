namespace Yijing.Application.Analysis;

public sealed record AnalysisTimeSettings
{
    public const double MinimumSeconds = 1;
    public const double MaximumSeconds = 600;
    public const int RampMoveCount = 150;

    public static AnalysisTimeSettings Default { get; } = new(5, 30);

    public AnalysisTimeSettings(double openingSeconds, double maxSeconds)
    {
        if (!double.IsFinite(openingSeconds) ||
            openingSeconds < MinimumSeconds || openingSeconds > MaximumSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(openingSeconds),
                $"开局思考时长必须在 {MinimumSeconds} 到 {MaximumSeconds} 秒之间。");
        }

        if (!double.IsFinite(maxSeconds) ||
            maxSeconds < MinimumSeconds || maxSeconds > MaximumSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxSeconds),
                $"思考时长上限必须在 {MinimumSeconds} 到 {MaximumSeconds} 秒之间。");
        }

        if (maxSeconds < openingSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxSeconds),
                "思考时长上限不能小于开局思考时长。");
        }

        OpeningSeconds = openingSeconds;
        MaxSeconds = maxSeconds;
    }

    public double OpeningSeconds { get; init; }

    public double MaxSeconds { get; init; }

    public TimeSpan DurationForMove(int moveNumber)
    {
        if (moveNumber < 1)
        {
            return TimeSpan.FromSeconds(OpeningSeconds);
        }

        var progress = Math.Min((moveNumber - 1) / (double)(RampMoveCount - 1), 1.0);
        var seconds = OpeningSeconds + (MaxSeconds - OpeningSeconds) * progress;
        return TimeSpan.FromSeconds(seconds);
    }
}
