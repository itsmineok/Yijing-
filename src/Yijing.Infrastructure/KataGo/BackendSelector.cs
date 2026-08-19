using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Yijing.Infrastructure.KataGo;

public sealed class BackendSelector
{
    public static readonly TimeSpan BenchmarkStartupTimeout = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan TensorRtBenchmarkStartupTimeout = TimeSpan.FromMinutes(10);

    private readonly EngineManifest _manifest;
    private readonly string _assetRoot;
    private readonly IEngineBenchmarkProbe _probe;
    private readonly Func<bool> _isAvx2Supported;
    private readonly string _profilePath;

    public BackendSelector(
        EngineManifest manifest,
        string assetRoot,
        IEngineBenchmarkProbe? probe = null,
        Func<bool>? isAvx2Supported = null,
        string? profilePath = null)
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _assetRoot = Path.GetFullPath(assetRoot ?? throw new ArgumentNullException(nameof(assetRoot)));
        _probe = probe ?? new KataGoBenchmarkProbe(_assetRoot);
        _isAvx2Supported = isAvx2Supported ?? (() => System.Runtime.Intrinsics.X86.Avx2.IsSupported);
        _profilePath = profilePath ?? Path.Combine(GetEngineCacheDirectory(), "engine-profile.json");
    }

    public async Task<EngineSelection> SelectAsync(
        EngineProfile? cachedProfile = null,
        CancellationToken cancellationToken = default)
    {
        cachedProfile ??= await LoadProfileAsync(cancellationToken).ConfigureAwait(false);
        return await SelectCoreAsync(cachedProfile, cancellationToken).ConfigureAwait(false);
    }

    public Task<EngineSelection> SelectFreshAsync(CancellationToken cancellationToken = default) =>
        SelectCoreAsync(cachedProfile: null, cancellationToken);

    private async Task<EngineSelection> SelectCoreAsync(
        EngineProfile? cachedProfile,
        CancellationToken cancellationToken)
    {
        if (cachedProfile is not null && IsCurrent(cachedProfile))
        {
            var cached = _manifest.Candidates.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, cachedProfile.CandidateName, StringComparison.OrdinalIgnoreCase));
            if (cached is not null)
            {
                var selection = await TryBenchmarkClassAsync([cached], cancellationToken).ConfigureAwait(false);
                if (selection is not null)
                {
                    await PersistProfileAsync(selection.Profile, cancellationToken).ConfigureAwait(false);
                    return selection;
                }
            }
        }

        foreach (var backend in BackendOrder)
        {
            var candidates = _manifest.Candidates
                .Where(candidate => candidate.Backend == backend)
                .OrderByDescending(candidate => candidate.Priority)
                .ToArray();
            var selection = await TryBenchmarkClassAsync(candidates, cancellationToken).ConfigureAwait(false);
            if (selection is null)
            {
                continue;
            }

            await PersistProfileAsync(selection.Profile, cancellationToken).ConfigureAwait(false);
            return selection;
        }

        throw new InvalidOperationException("No verified KataGo backend passed the benchmark.");
    }

    public static string GetEngineCacheDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Yijing",
        "engine-cache");

    private static EngineBackend[] BackendOrder =>
        [EngineBackend.TensorRt, EngineBackend.OpenCl, EngineBackend.EigenAvx2, EngineBackend.Eigen];

    private bool IsCurrent(EngineProfile profile)
    {
        var candidate = _manifest.Candidates.FirstOrDefault(item =>
            string.Equals(item.Name, profile.CandidateName, StringComparison.OrdinalIgnoreCase));
        return candidate is not null &&
            string.Equals(profile.ManifestFingerprint, _manifest.Fingerprint, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(profile.KataGoVersion, candidate.KataGoVersion, StringComparison.Ordinal) &&
            string.Equals(profile.AssetFingerprint, _manifest.GetAssetFingerprint(candidate),
                StringComparison.OrdinalIgnoreCase);
    }

    private async Task<EngineSelection?> TryBenchmarkClassAsync(
        IReadOnlyList<EngineCandidate> candidates,
        CancellationToken cancellationToken)
    {
        EngineSelection? best = null;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.RequiresAvx2 && !_isAvx2Supported())
            {
                continue;
            }

            if (!await HasValidDigestsAsync(candidate, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            EngineBenchmarkResult benchmark;
            try
            {
                var startupTimeout = candidate.Backend == EngineBackend.TensorRt
                    ? TensorRtBenchmarkStartupTimeout
                    : BenchmarkStartupTimeout;
                benchmark = await _probe
                    .BenchmarkAsync(candidate, startupTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or Win32Exception or InvalidOperationException)
            {
                continue;
            }
            if (!benchmark.IsSuccessful)
            {
                continue;
            }

            var profile = new EngineProfile(
                candidate.Name,
                benchmark.VisitsPerSecond,
                Math.Max(1, benchmark.NumSearchThreadsPerAnalysisThread),
                Math.Max(1, candidate.NnMaxBatchSize),
                _manifest.Fingerprint,
                candidate.KataGoVersion,
                _manifest.GetAssetFingerprint(candidate));
            if (best is null || profile.VisitsPerSecond > best.Profile.VisitsPerSecond)
            {
                best = new EngineSelection(candidate, profile);
            }
        }

        return best;
    }

    private async Task<bool> HasValidDigestsAsync(
        EngineCandidate candidate,
        CancellationToken cancellationToken)
    {
        var basicAssetsValid = await HasDigestAsync(candidate.Executable, candidate.Sha256.Executable, cancellationToken)
                .ConfigureAwait(false)
            && await HasDigestAsync(candidate.Model, candidate.Sha256.Model, cancellationToken)
                .ConfigureAwait(false)
            && await HasDigestAsync(candidate.Config, candidate.Sha256.Config, cancellationToken)
                .ConfigureAwait(false);
        if (!basicAssetsValid)
        {
            return false;
        }

        var download = _manifest.FindRuntimeDownload(candidate);
        return download is null || await HasExactRuntimeAsync(download, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> HasExactRuntimeAsync(
        EngineDownload download,
        CancellationToken cancellationToken)
    {
        if (download.RuntimeFiles.Count == 0)
        {
            return false;
        }

        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var runtimeFile in download.RuntimeFiles)
        {
            var relative = Path.Combine(download.Destination, runtimeFile.Path);
            expected.Add(Path.GetFullPath(ResolveAssetPath(relative)));
            if (!await HasDigestAsync(relative, runtimeFile.Sha256, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
        }

        var runtimeDirectory = ResolveAssetPath(download.Destination);
        if (!Directory.Exists(runtimeDirectory))
        {
            return false;
        }

        var actual = Directory.EnumerateFiles(runtimeDirectory, "*", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return actual.SetEquals(expected);
    }

    private async Task<bool> HasDigestAsync(
        string relativePath,
        string expectedDigest,
        CancellationToken cancellationToken)
    {
        var path = ResolveAssetPath(relativePath);
        if (!File.Exists(path) || expectedDigest.Length != 64)
        {
            return false;
        }

        byte[] expected;
        try
        {
            expected = Convert.FromHexString(expectedDigest);
        }
        catch (FormatException)
        {
            return false;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actual = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    internal string ResolveAssetPath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("Manifest asset paths must be relative.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(_assetRoot, relativePath));
        var rootPrefix = _assetRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Manifest asset path escapes the asset root.");
        }

        return fullPath;
    }

    private async Task<EngineProfile?> LoadProfileAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_profilePath))
        {
            return null;
        }

        try
        {
            return await EngineProfile.LoadAsync(_profilePath, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task PersistProfileAsync(EngineProfile profile, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_profilePath)
            ?? throw new InvalidOperationException("The engine profile path has no directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = _profilePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, profile, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporaryPath, _profilePath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}

public sealed record EngineBenchmarkProcessResult(int ExitCode, string Output);

public interface IEngineBenchmarkProcessRunner
{
    Task<EngineBenchmarkProcessResult> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public sealed partial class KataGoBenchmarkProbe : IEngineBenchmarkProbe
{
    private readonly string _assetRoot;
    private readonly IEngineBenchmarkProcessRunner _processRunner;
    private readonly string? _localApplicationData;

    public KataGoBenchmarkProbe(
        string assetRoot,
        IEngineBenchmarkProcessRunner? processRunner = null,
        string? localApplicationData = null)
    {
        _assetRoot = Path.GetFullPath(assetRoot);
        _processRunner = processRunner ?? new DefaultEngineBenchmarkProcessRunner();
        _localApplicationData = localApplicationData;
    }

    public async Task<EngineBenchmarkResult> BenchmarkAsync(
        EngineCandidate candidate,
        TimeSpan startupTimeout,
        CancellationToken cancellationToken)
    {
        var executable = Resolve(_assetRoot, candidate.Executable);
        var model = Resolve(_assetRoot, candidate.Model);
        var packagedConfig = Resolve(_assetRoot, candidate.Config);
        var config = await AnalysisConfiguration.WriteBenchmarkRuntimeAsync(
            packagedConfig,
            _localApplicationData,
            cancellationToken).ConfigureAwait(false);
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        EngineRuntime.ApplyToEnvironment(startInfo, _localApplicationData);
        startInfo.ArgumentList.Add("benchmark");
        startInfo.ArgumentList.Add("-model");
        startInfo.ArgumentList.Add(model);
        startInfo.ArgumentList.Add("-visits");
        startInfo.ArgumentList.Add("500");
        startInfo.ArgumentList.Add("-numpositions");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("-threads");
        startInfo.ArgumentList.Add("1,2,4,8");
        startInfo.ArgumentList.Add("-config");
        startInfo.ArgumentList.Add(config);

        var process = await _processRunner.RunAsync(startInfo, startupTimeout, cancellationToken)
            .ConfigureAwait(false);
        return ParseBenchmarkOutput(process.ExitCode, process.Output);
    }

    public static EngineBenchmarkResult ParseBenchmarkOutput(int exitCode, string output)
    {
        if (exitCode != 0)
        {
            return EngineBenchmarkResult.Failure(exitCode);
        }

        var bestVisits = 0d;
        var bestThreads = 0;
        foreach (Match match in BenchmarkLineRegex().Matches(output))
        {
            if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var threads) ||
                !double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var visits) ||
                threads <= 0 || visits <= bestVisits)
            {
                continue;
            }

            bestVisits = visits;
            bestThreads = threads;
        }

        return bestVisits > 0
            ? EngineBenchmarkResult.Success(bestVisits, bestThreads, bestThreads)
            : EngineBenchmarkResult.Failure(exitCode);
    }

    private static string Resolve(string root, string relativePath) =>
        Path.GetFullPath(Path.Combine(root, relativePath));

    [GeneratedRegex(@"(?im)numSearchThreads\s*=\s*(\d+).*?visits/s\s*=\s*([0-9]+(?:\.[0-9]+)?)")]
    private static partial Regex BenchmarkLineRegex();
}

internal sealed class DefaultEngineBenchmarkProcessRunner : IEngineBenchmarkProcessRunner
{
    public async Task<EngineBenchmarkProcessResult> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            return new EngineBenchmarkProcessResult(-1, "");
        }

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            return new EngineBenchmarkProcessResult(-1, "");
        }

        var output = string.Concat(await stdout.ConfigureAwait(false), "\n", await stderr.ConfigureAwait(false));
        return new EngineBenchmarkProcessResult(process.ExitCode, output);
    }
}
