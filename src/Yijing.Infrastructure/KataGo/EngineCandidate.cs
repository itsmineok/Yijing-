namespace Yijing.Infrastructure.KataGo;

public enum EngineBackend
{
    OpenCl,
    TensorRt,
    EigenAvx2,
    Eigen,
}

public sealed record EngineAssetDigests(
    string Executable,
    string Model,
    string Config);

public sealed record EngineCandidate
{
    public required string Name { get; init; }

    public string KataGoVersion { get; init; } = "";

    public required EngineBackend Backend { get; init; }

    public required string Executable { get; init; }

    public required string Model { get; init; }

    public required string Config { get; init; }

    public required EngineAssetDigests Sha256 { get; init; }

    public bool RequiresAvx2 { get; init; }

    public int Priority { get; init; }

    public int NnMaxBatchSize { get; init; } = 1;
}

public sealed record EngineProfile(
    string CandidateName,
    double VisitsPerSecond,
    int NumSearchThreadsPerAnalysisThread,
    int NnMaxBatchSize,
    string ManifestFingerprint = "",
    string KataGoVersion = "",
    string AssetFingerprint = "")
{
    public static async Task<EngineProfile?> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await System.Text.Json.JsonSerializer.DeserializeAsync<EngineProfile>(
            stream,
            cancellationToken: cancellationToken);
    }
}

public sealed record EngineSelection(EngineCandidate Candidate, EngineProfile Profile);

public sealed record EngineBenchmarkResult(
    int ExitCode,
    double VisitsPerSecond,
    int NumSearchThreadsPerAnalysisThread,
    int NnMaxBatchSize)
{
    public bool IsSuccessful => ExitCode == 0 && VisitsPerSecond > 0;

    public static EngineBenchmarkResult Success(
        double visitsPerSecond,
        int numSearchThreadsPerAnalysisThread,
        int nnMaxBatchSize) =>
        new(0, visitsPerSecond, numSearchThreadsPerAnalysisThread, nnMaxBatchSize);

    public static EngineBenchmarkResult Failure(int exitCode) => new(exitCode, 0, 0, 0);
}

public interface IEngineBenchmarkProbe
{
    Task<EngineBenchmarkResult> BenchmarkAsync(
        EngineCandidate candidate,
        TimeSpan startupTimeout,
        CancellationToken cancellationToken);
}

public static class AnalysisConfiguration
{
    private static readonly string[] RuntimeKeys =
    [
        "numAnalysisThreads",
        "numSearchThreads",
        "numSearchThreadsPerAnalysisThread",
        "nnMaxBatchSize",
        "reportAnalysisWinratesAs",
        "logToStderr",
        "homeDataDir",
        "openclTunerFile",
    ];

    public static async Task<string> WriteRuntimeAsync(
        string templatePath,
        EngineProfile profile,
        string? localApplicationData = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var cacheDirectory = GetCacheDirectory(localApplicationData);
        Directory.CreateDirectory(cacheDirectory);

        var template = await File.ReadAllLinesAsync(templatePath, cancellationToken).ConfigureAwait(false);
        var runtimePath = Path.Combine(cacheDirectory, "analysis.runtime.cfg");
        var retained = template.Where(line => !IsRuntimeKey(line)).ToList();
        retained.AddRange(
        [
            "",
            "# Generated at runtime; writable engine state remains under LocalAppData.",
            "numAnalysisThreads=1",
            $"numSearchThreadsPerAnalysisThread={Math.Max(1, profile.NumSearchThreadsPerAnalysisThread)}",
            $"nnMaxBatchSize={Math.Max(1, profile.NnMaxBatchSize)}",
            "reportAnalysisWinratesAs=SIDETOMOVE",
            "logToStderr=true",
            $"homeDataDir={ToConfigPath(cacheDirectory)}",
        ]);
        await WriteAllLinesAtomicallyAsync(runtimePath, retained, cancellationToken).ConfigureAwait(false);
        return runtimePath;
    }

    public static async Task<string> WriteBenchmarkRuntimeAsync(
        string templatePath,
        string? localApplicationData = null,
        CancellationToken cancellationToken = default)
    {
        var cacheDirectory = GetCacheDirectory(localApplicationData);
        Directory.CreateDirectory(cacheDirectory);
        var template = await File.ReadAllLinesAsync(templatePath, cancellationToken).ConfigureAwait(false);
        var retained = template.Where(line => !IsRuntimeKey(line)).ToList();
        retained.AddRange(
        [
            "",
            "# Generated for benchmark startup; writable KataGo data remains under LocalAppData.",
            "logToStderr=true",
            "numSearchThreads=1",
            $"homeDataDir={ToConfigPath(cacheDirectory)}",
        ]);
        var runtimePath = Path.Combine(cacheDirectory, "benchmark.runtime.cfg");
        await WriteAllLinesAtomicallyAsync(runtimePath, retained, cancellationToken).ConfigureAwait(false);
        return runtimePath;
    }

    private static bool IsRuntimeKey(string line)
    {
        var trimmed = line.TrimStart();
        return RuntimeKeys.Any(key =>
            trimmed.StartsWith(key, StringComparison.OrdinalIgnoreCase) &&
            trimmed.AsSpan(key.Length).TrimStart().StartsWith("="));
    }

    private static string GetCacheDirectory(string? localApplicationData)
    {
        var localRoot = localApplicationData
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.GetFullPath(Path.Combine(localRoot, "Yijing", "engine-cache"));
    }

    private static async Task WriteAllLinesAtomicallyAsync(
        string path,
        IReadOnlyCollection<string> lines,
        CancellationToken cancellationToken)
    {
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllLinesAsync(temporaryPath, lines, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static string ToConfigPath(string path) => path.Replace('\\', '/');
}
