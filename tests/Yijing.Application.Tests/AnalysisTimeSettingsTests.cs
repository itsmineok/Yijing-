using Yijing.Application.Analysis;

namespace Yijing.Application.Tests;

public sealed class AnalysisTimeSettingsTests
{
    [Fact]
    public void DurationForMove_FirstMoveReturnsOpeningDuration()
    {
        var settings = new AnalysisTimeSettings(5, 30);

        Assert.Equal(TimeSpan.FromSeconds(5), settings.DurationForMove(1));
        Assert.Equal(TimeSpan.FromSeconds(5), settings.DurationForMove(0));
    }

    [Fact]
    public void DurationForMove_RampEndpointReturnsMaxDuration()
    {
        var settings = new AnalysisTimeSettings(5, 30);

        Assert.Equal(TimeSpan.FromSeconds(30), settings.DurationForMove(150));
    }

    [Fact]
    public void DurationForMove_BeyondRampEndpointStaysAtMax()
    {
        var settings = new AnalysisTimeSettings(5, 30);

        Assert.Equal(TimeSpan.FromSeconds(30), settings.DurationForMove(250));
    }

    [Fact]
    public void DurationForMove_IsNonDecreasingAcrossMoves()
    {
        var settings = new AnalysisTimeSettings(5, 30);
        var durations = Enumerable.Range(1, 151)
            .Select(move => settings.DurationForMove(move))
            .ToArray();

        for (var index = 1; index < durations.Length; index++)
        {
            Assert.True(durations[index] >= durations[index - 1]);
        }
    }

    [Fact]
    public void DurationForMove_MidRampIsBetweenOpeningAndMax()
    {
        var settings = new AnalysisTimeSettings(10, 110);

        var duration = settings.DurationForMove(76);

        Assert.True(duration > TimeSpan.FromSeconds(10));
        Assert.True(duration < TimeSpan.FromSeconds(110));
    }

    [Fact]
    public void Ctor_RejectsMaxBelowOpening()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnalysisTimeSettings(30, 5));
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(5, 601)]
    [InlineData(-1, 30)]
    [InlineData(5, 0)]
    public void Ctor_RejectsValuesOutsideOneToSixHundredSeconds(double opening, double max)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnalysisTimeSettings(opening, max));
    }

    [Fact]
    public void Default_IsFiveSecondsOpeningThirtySecondsMax()
    {
        Assert.Equal(5, AnalysisTimeSettings.Default.OpeningSeconds);
        Assert.Equal(30, AnalysisTimeSettings.Default.MaxSeconds);
    }
}
