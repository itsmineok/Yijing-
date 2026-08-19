using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Yijing.Infrastructure.KataGo;

namespace Yijing.Infrastructure.Tests;

public sealed class BackendSelectorTests
{
    [Fact]
    public async Task CachedOpenClSuccessIsReturnedBeforeEigenIsProbed()
    {
        using var files = new EngineFiles();
        var probe = new FakeProbe(new Dictionary<string, EngineBenchmarkResult>
        {
            ["opencl"] = EngineBenchmarkResult.Success(410, 4, 8),
            ["eigen"] = EngineBenchmarkResult.Success(20, 1, 1),
        });
        var selector = files.CreateSelector(probe, avx2Supported: true);

        var selected = await selector.SelectAsync(files.CreateValidCachedProfile("opencl"));

        Assert.Equal("opencl", selected.Candidate.Name);
        Assert.Equal(["opencl"], probe.ProbedNames);
    }

    [Fact]
    public async Task TensorRtAndOpenClFailuresFallBackToEigenAvx2()
    {
        using var files = new EngineFiles();
        var probe = new FakeProbe(new Dictionary<string, EngineBenchmarkResult>
        {
            ["opencl"] = EngineBenchmarkResult.Failure(1),
            ["tensorrt"] = EngineBenchmarkResult.Failure(1),
            ["eigen-avx2"] = EngineBenchmarkResult.Success(95, 3, 2),
        });

        var selected = await files.CreateSelector(probe, avx2Supported: true).SelectAsync();

        Assert.Equal("eigen-avx2", selected.Candidate.Name);
        Assert.Equal(["tensorrt", "opencl", "eigen-avx2"], probe.ProbedNames);
        Assert.Equal(1, selected.Profile.NnMaxBatchSize);
    }

    [Fact]
    public async Task UnsupportedAvx2SkipsAvx2AndProbesGenericEigen()
    {
        using var files = new EngineFiles();
        var probe = new FakeProbe(new Dictionary<string, EngineBenchmarkResult>
        {
            ["opencl"] = EngineBenchmarkResult.Failure(1),
            ["tensorrt"] = EngineBenchmarkResult.Failure(1),
            ["eigen"] = EngineBenchmarkResult.Success(42, 2, 1),
        });

        var selected = await files.CreateSelector(probe, avx2Supported: false).SelectAsync();

        Assert.Equal("eigen", selected.Candidate.Name);
        Assert.DoesNotContain("eigen-avx2", probe.ProbedNames);
        Assert.Equal(["tensorrt", "opencl", "eigen"], probe.ProbedNames);
    }

    [Fact]
    public async Task Sha256MismatchRejectsCandidateBeforeProcessLaunch()
    {
        using var files = new EngineFiles();
        files.CorruptExecutable("tensorrt");
        var probe = new FakeProbe(new Dictionary<string, EngineBenchmarkResult>
        {
            ["opencl"] = EngineBenchmarkResult.Success(999, 8, 16),
            ["tensorrt"] = EngineBenchmarkResult.Success(200, 4, 8),
        });

        var selected = await files.CreateSelector(probe, avx2Supported: true).SelectAsync();

        Assert.Equal("opencl", selected.Candidate.Name);
        Assert.DoesNotContain("tensorrt", probe.ProbedNames);
    }

    [Fact]
    public async Task SelectsHighestVisitsPerSecondWithinFirstSuccessfulBackendClass()
    {
        using var files = new EngineFiles();
        files.AddCandidate("tensorrt-fast", EngineBackend.TensorRt, priority: 290);
        var probe = new FakeProbe(new Dictionary<string, EngineBenchmarkResult>
        {
            ["tensorrt"] = EngineBenchmarkResult.Success(120, 2, 99),
            ["tensorrt-fast"] = EngineBenchmarkResult.Success(250, 4, 99),
            ["opencl"] = EngineBenchmarkResult.Success(900, 8, 99),
        });

        var selected = await files.CreateSelector(probe, avx2Supported: true).SelectAsync();

        Assert.Equal("tensorrt-fast", selected.Candidate.Name);
        Assert.Equal(["tensorrt", "tensorrt-fast"], probe.ProbedNames);
        Assert.Equal(8, selected.Profile.NnMaxBatchSize);
    }

    [Fact]
    public async Task ZeroVisitsPerSecondIsRejectedAndProfileIsPersistedForSuccess()
    {
        using var files = new EngineFiles();
        var probe = new FakeProbe(new Dictionary<string, EngineBenchmarkResult>
        {
            ["tensorrt"] = new EngineBenchmarkResult(0, 0, 8, 16),
            ["opencl"] = EngineBenchmarkResult.Success(200, 4, 99),
        });

        var selected = await files.CreateSelector(probe, avx2Supported: true).SelectAsync();

        Assert.Equal("opencl", selected.Candidate.Name);
        Assert.Equal(4, selected.Profile.NnMaxBatchSize);
        var persisted = await EngineProfile.LoadAsync(files.ProfilePath);
        Assert.Equal(selected.Profile, persisted);
    }

    [Fact]
    public async Task RuntimeConfigWritesTuningAndCacheOnlyUnderLocalAppData()
    {
        using var files = new EngineFiles();
        var template = Path.Combine(files.Root, "packaged-analysis.cfg");
        await File.WriteAllTextAsync(template,
            "numAnalysisThreads=9\nnumSearchThreadsPerAnalysisThread=99\nnnMaxBatchSize=99\n" +
            "reportAnalysisWinratesAs=BLACK\nlogToStderr=false\nopenclTunerFile=bad.txt\n");
        var localAppData = Path.Combine(files.Root, "LocalAppData");

        var path = await AnalysisConfiguration.WriteRuntimeAsync(
            template,
            new EngineProfile("opencl", 200, 6, 12),
            localAppData);

        Assert.StartsWith(Path.GetFullPath(localAppData), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
        var config = await File.ReadAllTextAsync(path);
        Assert.Equal(1, CountKey(config, "numSearchThreadsPerAnalysisThread"));
        Assert.Equal(1, CountKey(config, "nnMaxBatchSize"));
        Assert.Equal(1, CountKey(config, "numAnalysisThreads"));
        Assert.Equal(1, CountKey(config, "reportAnalysisWinratesAs"));
        Assert.Equal(1, CountKey(config, "logToStderr"));
        Assert.Equal(1, CountKey(config, "homeDataDir"));
        Assert.Contains("numSearchThreadsPerAnalysisThread=6", config);
        Assert.Contains("nnMaxBatchSize=12", config);
        Assert.Contains("numAnalysisThreads=1", config);
        Assert.Contains("reportAnalysisWinratesAs=SIDETOMOVE", config);
        Assert.Contains("logToStderr=true", config);
        Assert.DoesNotContain("openclTunerFile", config, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Program Files", config, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.Combine(localAppData, "Yijing", "engine-cache").Replace('\\', '/'), config);
    }

    [Fact]
    public void BenchmarkOutputUsesFastestPositiveVisitsLineAndItsThreadCount()
    {
        const string output = """
            numSearchThreads =  4: 10 / 10 positions, visits/s = 180.25 nnEvals/s = 90.0 avgBatchSize = 3.2
            numSearchThreads =  8: 10 / 10 positions, visits/s = 310.50 nnEvals/s = 120.0 avgBatchSize = 6.1
            numSearchThreads = 16: 10 / 10 positions, visits/s = 270.00 nnEvals/s = 130.0 avgBatchSize = 9.0
            """;

        var result = KataGoBenchmarkProbe.ParseBenchmarkOutput(0, output);

        Assert.True(result.IsSuccessful);
        Assert.Equal(310.50, result.VisitsPerSecond);
        Assert.Equal(8, result.NumSearchThreadsPerAnalysisThread);
        Assert.Equal(8, result.NnMaxBatchSize);
    }

    [Fact]
    public async Task ProcessLaunchFailureFallsBackToNextBackend()
    {
        using var files = new EngineFiles();
        var probe = new FakeProbe(new Dictionary<string, EngineBenchmarkResult>
        {
            ["opencl"] = EngineBenchmarkResult.Success(190, 4, 4),
        }, throwFor: "tensorrt");

        var selected = await files.CreateSelector(probe, avx2Supported: true).SelectAsync();

        Assert.Equal("opencl", selected.Candidate.Name);
        Assert.Equal(["tensorrt", "opencl"], probe.ProbedNames);
        Assert.Equal(4, selected.Profile.NnMaxBatchSize);
    }

    [Fact]
    public async Task CachedProfileWithStaleManifestFingerprintIsIgnored()
    {
        using var files = new EngineFiles();
        var probe = new FakeProbe(new Dictionary<string, EngineBenchmarkResult>
        {
            ["tensorrt"] = EngineBenchmarkResult.Success(300, 4, 4),
            ["opencl"] = EngineBenchmarkResult.Success(100, 2, 2),
        });
        var stale = files.CreateValidCachedProfile("opencl") with { ManifestFingerprint = "stale" };

        var selected = await files.CreateSelector(probe, avx2Supported: true).SelectAsync(stale);

        Assert.Equal("tensorrt", selected.Candidate.Name);
        Assert.Equal(["tensorrt"], probe.ProbedNames);
    }

    [Fact]
    public async Task CachedProfileWithStaleAssetFingerprintIsIgnored()
    {
        using var files = new EngineFiles();
        var probe = new FakeProbe(new Dictionary<string, EngineBenchmarkResult>
        {
            ["tensorrt"] = EngineBenchmarkResult.Success(300, 4, 4),
            ["opencl"] = EngineBenchmarkResult.Success(100, 2, 2),
        });
        var stale = files.CreateValidCachedProfile("opencl") with { AssetFingerprint = "stale" };

        var selected = await files.CreateSelector(probe, avx2Supported: true).SelectAsync(stale);

        Assert.Equal("tensorrt", selected.Candidate.Name);
        Assert.Equal(["tensorrt"], probe.ProbedNames);
    }

    [Fact]
    public async Task UnexpectedRuntimeFileRejectsCandidateBeforeLaunch()
    {
        using var files = new EngineFiles(includeRuntimeManifest: true);
        File.WriteAllText(Path.Combine(files.Root, "tensorrt", "old.dll"), "stale");
        var probe = new FakeProbe(new Dictionary<string, EngineBenchmarkResult>
        {
            ["tensorrt"] = EngineBenchmarkResult.Success(300, 4, 4),
            ["opencl"] = EngineBenchmarkResult.Success(100, 2, 2),
        });

        var selected = await files.CreateSelector(probe, avx2Supported: true).SelectAsync();

        Assert.Equal("opencl", selected.Candidate.Name);
        Assert.DoesNotContain("tensorrt", probe.ProbedNames);
    }

    [Fact]
    public async Task FetchRejectsArchiveWhenExpectedDigestIsReplaced()
    {
        using var fixture = new FetchFixture(expectedDigestOverride: new string('0', 64));

        var result = await fixture.RunFetchAsync();

        Assert.True(result.ExitCode != 0 &&
            result.Output.Contains("SHA-256 mismatch", StringComparison.OrdinalIgnoreCase), result.Output);
        Assert.True(File.Exists(fixture.OldMarkerPath));
    }

    [Fact]
    public async Task FetchAtomicallyReplacesOldRuntimeWithoutResidualFiles()
    {
        using var fixture = new FetchFixture();

        var result = await fixture.RunFetchAsync();

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.False(File.Exists(fixture.OldMarkerPath));
        Assert.Equal("fixture-engine", await File.ReadAllTextAsync(fixture.InstalledExecutablePath));
        Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(fixture.InstalledExecutablePath)!, "*",
            SearchOption.AllDirectories));
    }

    [Fact]
    public async Task VerifyRejectsUnexpectedRuntimeFileEvenWhenMissingDownloadsAreAllowed()
    {
        using var fixture = new FetchFixture();
        var fetched = await fixture.RunFetchAsync();
        Assert.True(fetched.ExitCode == 0, fetched.Output);
        await File.WriteAllTextAsync(fixture.OldMarkerPath, "unexpected");

        var verified = await fixture.RunVerifyAsync(allowMissing: true);

        Assert.True(verified.ExitCode != 0 &&
            verified.Output.Contains("not in the committed allow-list", StringComparison.OrdinalIgnoreCase),
            verified.Output);
    }

    [Fact]
    public async Task CommittedManifestLoadsExactOfficialBackendsAndRuntimeAllowLists()
    {
        var manifestPath = FindRepositoryFile("assets", "katago", "engine-manifest.json");

        var manifest = await EngineManifest.LoadAsync(manifestPath);

        Assert.Equal([EngineBackend.TensorRt, EngineBackend.OpenCl, EngineBackend.EigenAvx2, EngineBackend.Eigen],
            manifest.Candidates.Select(candidate => candidate.Backend));
        Assert.Equal([8, 4, 1, 1], manifest.Candidates.Select(candidate => candidate.NnMaxBatchSize));
        Assert.Equal(90, manifest.Downloads.Sum(download => download.RuntimeFiles.Count));
        Assert.All(manifest.Downloads, download =>
        {
            Assert.Matches("^[0-9a-f]{64}$", download.ExpectedArchiveSha256);
            Assert.StartsWith("https://github.com/lightvector/KataGo/releases/download/", download.Url,
                StringComparison.Ordinal);
        });
        Assert.Equal("7919b1a91fdd42fddac098ae8a98b68612354998e7c2c25b4102598f848a4c63",
            manifest.Downloads[0].ExpectedArchiveSha256);
        Assert.Equal("68d0a9b11ef7e3c1ddfc5bcd400306ca66c3770dd67a22cb377d3aaaf32e8c66",
            manifest.Downloads[1].ExpectedArchiveSha256);
    }

    [Fact]
    public async Task BenchmarkProbeKeepsEngineRuntimeImmutableAndWritesTuningUnderLocalAppData()
    {
        using var files = new EngineFiles();
        var runtimeDirectory = Path.Combine(files.Root, "opencl");
        var before = Directory.EnumerateFiles(runtimeDirectory, "*", SearchOption.AllDirectories)
            .Select(Path.GetFullPath).Order().ToArray();
        var localAppData = Path.Combine(files.Root, "LocalAppData");
        var runner = new FakeBenchmarkProcessRunner(localAppData);
        var candidate = files.GetCandidate("opencl");
        var probe = new KataGoBenchmarkProbe(files.Root, runner, localAppData);

        var result = await probe.BenchmarkAsync(candidate, TimeSpan.FromSeconds(90), CancellationToken.None);

        Assert.True(result.IsSuccessful);
        var after = Directory.EnumerateFiles(runtimeDirectory, "*", SearchOption.AllDirectories)
            .Select(Path.GetFullPath).Order().ToArray();
        Assert.Equal(before, after);
        Assert.True(File.Exists(Path.Combine(localAppData, "Yijing", "engine-cache", "opencl-tuning.txt")));
        Assert.StartsWith(Path.GetFullPath(localAppData), runner.HomeDataDirectory,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SelectAsync_UsesExtendedStartupTimeoutForTensorRtCandidate()
    {
        using var files = new EngineFiles();
        var probe = new FakeProbe(new Dictionary<string, EngineBenchmarkResult>
        {
            ["tensorrt"] = EngineBenchmarkResult.Success(500, 4, 8),
        });

        var selected = await files.CreateSelector(probe, avx2Supported: true).SelectAsync();

        Assert.Equal("tensorrt", selected.Candidate.Name);
        Assert.Equal(TimeSpan.FromMinutes(10), Assert.Single(probe.Probed).Timeout);
    }

    [Fact]
    public async Task SelectFreshAsync_IgnoresPersistedProfileAndProbesAllCandidates()
    {
        using var files = new EngineFiles();
        var probe = new FakeProbe(new Dictionary<string, EngineBenchmarkResult>
        {
            ["opencl"] = EngineBenchmarkResult.Success(410, 4, 8),
            ["tensorrt"] = EngineBenchmarkResult.Success(900, 4, 8),
        });
        var selector = files.CreateSelector(probe, avx2Supported: true);
        await File.WriteAllTextAsync(files.ProfilePath,
            JsonSerializer.Serialize(files.CreateValidCachedProfile("opencl")));

        var selected = await selector.SelectFreshAsync();

        Assert.Equal("tensorrt", selected.Candidate.Name);
        Assert.Equal(["tensorrt"], probe.ProbedNames);
    }

    [Fact]
    public async Task BenchmarkProbePrependsEngineRuntimeDirectoryToChildPath()
    {
        using var files = new EngineFiles();
        var localAppData = Path.Combine(files.Root, "LocalAppData");
        var runtimeDirectory = EngineRuntime.GetRuntimeDirectory(localAppData);
        Directory.CreateDirectory(runtimeDirectory);
        var runner = new FakeBenchmarkProcessRunner(localAppData);
        var candidate = files.GetCandidate("opencl");
        var probe = new KataGoBenchmarkProbe(files.Root, runner, localAppData);

        var result = await probe.BenchmarkAsync(candidate, TimeSpan.FromSeconds(90), CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.NotNull(runner.ReceivedPath);
        Assert.StartsWith(runtimeDirectory + ";", runner.ReceivedPath, StringComparison.OrdinalIgnoreCase);
    }

    private static int CountKey(string config, string key) => config
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Count(line => line.TrimStart().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));

    private sealed class FetchFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"yijing-fetch-{Guid.NewGuid():N}");
        private readonly string _manifestPath;
        private readonly string _downloadDirectory;

        public FetchFixture(string? expectedDigestOverride = null)
        {
            Directory.CreateDirectory(_root);
            _downloadDirectory = Directory.CreateDirectory(Path.Combine(_root, "downloads")).FullName;
            var source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
            var executable = Path.Combine(source, "katago.exe");
            File.WriteAllText(executable, "fixture-engine");
            var archive = Path.Combine(_downloadDirectory, "fixture.zip");
            ZipFile.CreateFromDirectory(source, archive);
            var archiveDigest = Hash(archive);
            var executableDigest = Hash(executable);

            Directory.CreateDirectory(Path.Combine(_root, "engines", "fixture"));
            File.WriteAllText(OldMarkerPath, "old");
            _manifestPath = Path.Combine(_root, "engine-manifest.json");
            var manifest = new
            {
                kataGoVersion = "v1.17.2",
                downloads = new[]
                {
                    new
                    {
                        name = "fixture.zip",
                        url = "https://github.com/lightvector/KataGo/releases/download/v1.17.2/fixture.zip",
                        kind = "Archive",
                        destination = "engines/fixture",
                        expectedArchiveSha256 = expectedDigestOverride ?? archiveDigest,
                        runtimeFiles = new[] { new { path = "katago.exe", sha256 = executableDigest } },
                    },
                },
                candidates = Array.Empty<object>(),
            };
            File.WriteAllText(_manifestPath, JsonSerializer.Serialize(manifest));
        }

        public string OldMarkerPath => Path.Combine(_root, "engines", "fixture", "old.txt");
        public string InstalledExecutablePath => Path.Combine(_root, "engines", "fixture", "katago.exe");

        public async Task<(int ExitCode, string Output)> RunFetchAsync()
        {
            var script = BackendSelectorTests.FindRepositoryFile("scripts", "Fetch-KataGoAssets.ps1");
            return await RunPowerShellAsync(script,
                "-Manifest", _manifestPath,
                "-DownloadDirectory", _downloadDirectory,
                "-UseExistingDownloads");
        }

        public async Task<(int ExitCode, string Output)> RunVerifyAsync(bool allowMissing)
        {
            var script = BackendSelectorTests.FindRepositoryFile("scripts", "Verify-KataGoAssets.ps1");
            var arguments = new List<string> { "-Manifest", _manifestPath };
            if (allowMissing)
            {
                arguments.Add("-AllowMissingDownloadedAssets");
            }
            return await RunPowerShellAsync(script, arguments.ToArray());
        }

        private static async Task<(int ExitCode, string Output)> RunPowerShellAsync(
            string script,
            params string[] arguments)
        {
            var startInfo = new ProcessStartInfo("pwsh.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(script);
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return (process.ExitCode, await stdout + await stderr);
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);

        private static string Hash(string path) =>
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(path))
            {
                return path;
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Repository file was not found.");
    }

    private sealed class FakeProbe(
        IReadOnlyDictionary<string, EngineBenchmarkResult> results,
        string? throwFor = null) : IEngineBenchmarkProbe
    {
        public List<string> ProbedNames { get; } = [];

        public List<(string Name, TimeSpan Timeout)> Probed { get; } = [];

        public Task<EngineBenchmarkResult> BenchmarkAsync(
            EngineCandidate candidate,
            TimeSpan startupTimeout,
            CancellationToken cancellationToken)
        {
            ProbedNames.Add(candidate.Name);
            Probed.Add((candidate.Name, startupTimeout));
            var expected = candidate.Backend == EngineBackend.TensorRt
                ? BackendSelector.TensorRtBenchmarkStartupTimeout
                : BackendSelector.BenchmarkStartupTimeout;
            Assert.Equal(expected, startupTimeout);
            if (string.Equals(candidate.Name, throwFor, StringComparison.Ordinal))
            {
                throw new IOException("Process launch failed");
            }
            return Task.FromResult(results.TryGetValue(candidate.Name, out var result)
                ? result
                : EngineBenchmarkResult.Failure(1));
        }
    }

    private sealed class FakeBenchmarkProcessRunner(string expectedLocalAppData) : IEngineBenchmarkProcessRunner
    {
        public string HomeDataDirectory { get; private set; } = "";

        public string? ReceivedPath { get; private set; }

        public async Task<EngineBenchmarkProcessResult> RunAsync(
            ProcessStartInfo startInfo,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Assert.Equal(TimeSpan.FromSeconds(90), timeout);
            ReceivedPath = startInfo.Environment.TryGetValue("PATH", out var path) ? path : null;
            var arguments = startInfo.ArgumentList.ToArray();
            Assert.Contains("-visits", arguments);
            Assert.Contains("500", arguments);
            Assert.Contains("-numpositions", arguments);
            Assert.Contains("1", arguments);
            Assert.Contains("-threads", arguments);
            Assert.Contains("1,2,4,8", arguments);
            var configIndex = Array.IndexOf(arguments, "-config");
            Assert.True(configIndex >= 0);
            var config = await File.ReadAllLinesAsync(arguments[configIndex + 1], cancellationToken);
            var homeData = Assert.Single(config, line =>
                line.StartsWith("homeDataDir=", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("numSearchThreads=1", Assert.Single(config, line =>
                line.StartsWith("numSearchThreads=", StringComparison.OrdinalIgnoreCase)));
            HomeDataDirectory = homeData[(homeData.IndexOf('=') + 1)..].Replace('/', Path.DirectorySeparatorChar);
            Assert.StartsWith(Path.GetFullPath(expectedLocalAppData), Path.GetFullPath(HomeDataDirectory),
                StringComparison.OrdinalIgnoreCase);
            Directory.CreateDirectory(HomeDataDirectory);
            await File.WriteAllTextAsync(Path.Combine(HomeDataDirectory, "opencl-tuning.txt"), "tuned",
                cancellationToken);
            return new EngineBenchmarkProcessResult(0,
                "numSearchThreads = 4: 10 / 10 positions, visits/s = 123.45");
        }
    }

    private sealed class EngineFiles : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"yijing-engine-{Guid.NewGuid():N}");
        private readonly List<EngineCandidate> _candidates = [];

        private readonly EngineManifest _manifest;

        public EngineFiles(bool includeRuntimeManifest = false)
        {
            Directory.CreateDirectory(_root);
            Add("opencl", EngineBackend.OpenCl, priority: 400);
            Add("tensorrt", EngineBackend.TensorRt, priority: 300);
            Add("eigen-avx2", EngineBackend.EigenAvx2, priority: 200, requiresAvx2: true);
            Add("eigen", EngineBackend.Eigen, priority: 100);
            _manifest = new EngineManifest("v1.17.2", _candidates)
            {
                Downloads = includeRuntimeManifest
                    ? [CreateRuntimeDownload("tensorrt")]
                    : [],
            };
        }

        public string Root => _root;

        public string ProfilePath => Path.Combine(_root, "profile.json");

        public BackendSelector CreateSelector(IEngineBenchmarkProbe probe, bool avx2Supported) =>
            new(_manifest, _root, probe, () => avx2Supported,
                ProfilePath);

        public EngineProfile CreateValidCachedProfile(string candidateName)
        {
            var candidate = _candidates.Single(candidate => candidate.Name == candidateName);
            return new EngineProfile(candidateName, 1, 1, candidate.NnMaxBatchSize,
                _manifest.Fingerprint, candidate.KataGoVersion, _manifest.GetAssetFingerprint(candidate));
        }

        public EngineCandidate GetCandidate(string name) => _candidates.Single(candidate => candidate.Name == name);

        public void CorruptExecutable(string name) =>
            File.AppendAllText(Path.Combine(_root, name, "katago.exe"), "corrupt");

        public void Dispose() => Directory.Delete(_root, recursive: true);

        public void AddCandidate(string name, EngineBackend backend, int priority, bool requiresAvx2 = false) =>
            Add(name, backend, priority, requiresAvx2);

        private void Add(string name, EngineBackend backend, int priority, bool requiresAvx2 = false)
        {
            var directory = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
            var executable = Write(directory, "katago.exe", $"{name}-exe");
            var model = Write(directory, "model.bin.gz", $"{name}-model");
            var config = Write(directory, "analysis.cfg", "numAnalysisThreads=1");
            _candidates.Add(new EngineCandidate
            {
                Name = name,
                KataGoVersion = "v1.17.2",
                Backend = backend,
                Executable = Path.GetRelativePath(_root, executable),
                Model = Path.GetRelativePath(_root, model),
                Config = Path.GetRelativePath(_root, config),
                Sha256 = new EngineAssetDigests(Hash(executable), Hash(model), Hash(config)),
                RequiresAvx2 = requiresAvx2,
                Priority = priority,
                NnMaxBatchSize = backend switch
                {
                    EngineBackend.TensorRt => 8,
                    EngineBackend.OpenCl => 4,
                    _ => 1,
                },
            });
        }

        private EngineDownload CreateRuntimeDownload(string name)
        {
            var executable = Path.Combine(_root, name, "katago.exe");
            return new EngineDownload
            {
                Name = name + ".zip",
                Url = "https://github.com/lightvector/KataGo/releases/download/v1.17.2/" + name + ".zip",
                Kind = EngineDownloadKind.Archive,
                Destination = name,
                ExpectedArchiveSha256 = new string('a', 64),
                RuntimeFiles = [new EngineRuntimeFile("katago.exe", Hash(executable))],
            };
        }

        private static string Write(string directory, string name, string content)
        {
            var path = Path.Combine(directory, name);
            File.WriteAllText(path, content);
            return path;
        }

        private static string Hash(string path) =>
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    }
}
