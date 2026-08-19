using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Yijing.Infrastructure.KataGo;

public enum EngineDownloadKind
{
    Archive,
    File,
}

public sealed record EngineRuntimeFile(string Path, string Sha256);

public sealed record EngineDownload
{
    public required string Name { get; init; }
    public required string Url { get; init; }
    public required EngineDownloadKind Kind { get; init; }
    public required string Destination { get; init; }
    public required string ExpectedArchiveSha256 { get; init; }
    public IReadOnlyList<EngineRuntimeFile> RuntimeFiles { get; init; } = [];
}

public sealed record EngineManifest(string KataGoVersion, IReadOnlyList<EngineCandidate> Candidates)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public IReadOnlyList<EngineDownload> Downloads { get; init; } = [];

    [JsonIgnore]
    public string Fingerprint => Hash(JsonSerializer.Serialize(new
    {
        KataGoVersion,
        Candidates = Candidates.OrderBy(candidate => candidate.Name, StringComparer.Ordinal).ToArray(),
        Downloads = Downloads
            .OrderBy(download => download.Name, StringComparer.Ordinal)
            .Select(download => download with
            {
                RuntimeFiles = download.RuntimeFiles.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray(),
            })
            .ToArray(),
    }, JsonOptions));

    public static async Task<EngineManifest> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<EngineManifest>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The KataGo engine manifest is empty.");
    }

    public string GetAssetFingerprint(EngineCandidate candidate)
    {
        var runtime = FindRuntimeDownload(candidate);
        return Hash(JsonSerializer.Serialize(new
        {
            candidate.Executable,
            candidate.Model,
            candidate.Config,
            candidate.Sha256,
            candidate.NnMaxBatchSize,
            Runtime = runtime,
        }, JsonOptions));
    }

    public EngineDownload? FindRuntimeDownload(EngineCandidate candidate)
    {
        var executable = Normalize(candidate.Executable);
        return Downloads.FirstOrDefault(download =>
            download.Kind == EngineDownloadKind.Archive &&
            executable.StartsWith(Normalize(download.Destination).TrimEnd('/') + '/',
                StringComparison.OrdinalIgnoreCase));
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Normalize(string value) => value.Replace('\\', '/');
}
