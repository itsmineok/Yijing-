using Yijing.Application.Analysis;
using Yijing.Infrastructure.Storage;

namespace Yijing.Infrastructure.Tests;

public sealed class AnalysisSettingsStoreTests
{
    [Fact]
    public async Task LoadAsync_NoFileReturnsDefault()
    {
        using var directory = new TemporaryDirectory();
        var store = new AnalysisSettingsStore(directory.Path);

        var settings = await store.LoadAsync();

        Assert.Equal(AnalysisTimeSettings.Default, settings);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsValues()
    {
        using var directory = new TemporaryDirectory();
        var store = new AnalysisSettingsStore(directory.Path);
        var expected = new AnalysisTimeSettings(8, 60);

        await store.SaveAsync(expected);

        var actual = await store.LoadAsync();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task LoadAsync_CorruptJsonReturnsDefault()
    {
        using var directory = new TemporaryDirectory();
        var store = new AnalysisSettingsStore(directory.Path);
        Directory.CreateDirectory(AnalysisSettingsStore.GetSettingsDirectory(directory.Path));
        await File.WriteAllTextAsync(
            Path.Combine(AnalysisSettingsStore.GetSettingsDirectory(directory.Path), "settings.json"),
            "{ not valid json !!!");

        var settings = await store.LoadAsync();

        Assert.Equal(AnalysisTimeSettings.Default, settings);
    }

    [Fact]
    public async Task LoadAsync_InvalidValuesReturnsDefault()
    {
        using var directory = new TemporaryDirectory();
        var store = new AnalysisSettingsStore(directory.Path);
        Directory.CreateDirectory(AnalysisSettingsStore.GetSettingsDirectory(directory.Path));
        await File.WriteAllTextAsync(
            Path.Combine(AnalysisSettingsStore.GetSettingsDirectory(directory.Path), "settings.json"),
            """{"openingSeconds":60,"maxSeconds":5}""");

        var settings = await store.LoadAsync();

        Assert.Equal(AnalysisTimeSettings.Default, settings);
    }

    [Fact]
    public async Task SaveAsync_WritesUnderSettingsDirectory()
    {
        using var directory = new TemporaryDirectory();
        var store = new AnalysisSettingsStore(directory.Path);

        await store.SaveAsync(new AnalysisTimeSettings(5, 30));

        var settingsPath = Path.Combine(
            AnalysisSettingsStore.GetSettingsDirectory(directory.Path), "settings.json");
        Assert.True(File.Exists(settingsPath));
    }
}
