using Yijing.Application.Analysis;
using Yijing.Desktop.ViewModels;
using Yijing.Infrastructure.KataGo;

namespace Yijing.Desktop.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public void Apply_ValidValuesInvokesCallbackWithSettings()
    {
        var applied = new List<AnalysisTimeSettings>();
        var viewModel = new SettingsViewModel(new AnalysisTimeSettings(5, 30), applied.Add)
        {
            OpeningSeconds = 8,
            MaxSeconds = 60,
        };

        viewModel.Apply();

        var settings = Assert.Single(applied);
        Assert.Equal(8, settings.OpeningSeconds);
        Assert.Equal(60, settings.MaxSeconds);
        Assert.Equal("", viewModel.ErrorText);
    }

    [Fact]
    public void Apply_MaxBelowOpeningSetsErrorAndDoesNotInvokeCallback()
    {
        var invoked = 0;
        var viewModel = new SettingsViewModel(new AnalysisTimeSettings(5, 30), _ => invoked++)
        {
            OpeningSeconds = 60,
            MaxSeconds = 5,
        };

        viewModel.Apply();

        Assert.Equal(0, invoked);
        Assert.NotEqual("", viewModel.ErrorText);
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(5, 601)]
    public void Apply_OutOfRangeValuesSetError(double opening, double max)
    {
        var invoked = 0;
        var viewModel = new SettingsViewModel(new AnalysisTimeSettings(5, 30), _ => invoked++)
        {
            OpeningSeconds = opening,
            MaxSeconds = max,
        };

        viewModel.Apply();

        Assert.Equal(0, invoked);
        Assert.NotEqual("", viewModel.ErrorText);
    }

    [Fact]
    public async Task RebenchmarkCommand_ReportsStatusAndFinalSelection()
    {
        var selection = new EngineSelection(
            new EngineCandidate
            {
                Name = "TensorRT 10.16.1 CUDA 13.2",
                Backend = EngineBackend.TensorRt,
                Executable = "engines/tensorrt/katago.exe",
                Model = "models/model.bin.gz",
                Config = "analysis.cfg",
                Sha256 = new EngineAssetDigests(
                    new string('a', 64), new string('b', 64), new string('c', 64)),
            },
            new EngineProfile("TensorRT 10.16.1 CUDA 13.2", 1234, 4, 8));
        var viewModel = new SettingsViewModel(
            new AnalysisTimeSettings(5, 30),
            _ => { },
            () => Task.FromResult(selection));

        await viewModel.RebenchmarkCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsRebenchmarking);
        Assert.Contains("TensorRT", viewModel.RebenchmarkStatusText);
        Assert.Contains("1234", viewModel.RebenchmarkStatusText);
    }

    [Fact]
    public async Task RebenchmarkCommand_FailureReportsErrorStatus()
    {
        var viewModel = new SettingsViewModel(
            new AnalysisTimeSettings(5, 30),
            _ => { },
            () => Task.FromException<EngineSelection>(new InvalidOperationException("No verified backend.")));

        await viewModel.RebenchmarkCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsRebenchmarking);
        Assert.Contains("失败", viewModel.RebenchmarkStatusText);
        Assert.Contains("No verified backend.", viewModel.RebenchmarkStatusText);
    }

    [Fact]
    public void RebenchmarkCommand_IsDisabledWithoutCallback()
    {
        var viewModel = new SettingsViewModel(new AnalysisTimeSettings(5, 30), _ => { });

        Assert.False(viewModel.RebenchmarkCommand.CanExecute(null));
    }
}
