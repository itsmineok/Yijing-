using System.Text.Json;
using Yijing.Application.Analysis;

namespace Yijing.Infrastructure.Storage;

public sealed class AnalysisSettingsStore
{
    private const string SettingsFileName = "settings.json";
    private readonly AtomicJsonStore _store;

    public AnalysisSettingsStore(string? localAppData = null)
    {
        var appData = localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _store = new AtomicJsonStore(GetSettingsDirectory(appData));
    }

    public static string GetSettingsDirectory(string localAppData) =>
        Path.GetFullPath(Path.Combine(localAppData, "Yijing", "settings"));

    public async Task<AnalysisTimeSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _store.ReadAsync<AnalysisTimeSettings>(SettingsFileName, cancellationToken)
                   ?? AnalysisTimeSettings.Default;
        }
        catch (JsonException)
        {
            return AnalysisTimeSettings.Default;
        }
        catch (ArgumentException)
        {
            return AnalysisTimeSettings.Default;
        }
    }

    public Task SaveAsync(AnalysisTimeSettings settings, CancellationToken cancellationToken = default) =>
        _store.WriteAsync(SettingsFileName, settings, cancellationToken);
}
