using System.Diagnostics;
using System.IO;
using System.Windows;
using Yijing.Application.Analysis;
using Yijing.Application.Games;
using Yijing.Desktop.Services;
using Yijing.Desktop.ViewModels;
using Yijing.Infrastructure.Diagnostics;
using Yijing.Infrastructure.KataGo;
using Yijing.Infrastructure.Storage;
using Yijing.Domain.Board;

namespace Yijing.Desktop;

/// <summary>
/// Interaction logic for App.xaml.
/// </summary>
public partial class App : System.Windows.Application
{
    private readonly GameOptions _defaultOptions = new(GameMode.HumanVsAi, 19, StoneColor.Black, 7.5);
    private readonly TaskCompletionSource _engineInitialized =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile EngineStartupState _engineState = EngineStartupState.Detecting;
    private volatile EngineStartupState _selectedEngineState = EngineStartupState.Ready;
    private volatile AnalysisTimeSettings _analysisSettings = AnalysisTimeSettings.Default;
    private AnalysisSettingsStore _settingsStore = null!;
    private SwitchablePositionAnalyzer _switchable = null!;
    private MainWindowViewModel _shell = null!;
    private RollingFileLogger _logger = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var logger = new RollingFileLogger();
        _logger = logger;
        _settingsStore = new AnalysisSettingsStore();
        try
        {
            _analysisSettings = await _settingsStore.LoadAsync();
        }
        catch (Exception)
        {
            _analysisSettings = AnalysisTimeSettings.Default;
        }
        var store = new LocalGameStore();
        var dialogs = new DialogService(() => MainWindow);
        var switchableAnalyzer = new SwitchablePositionAnalyzer();
        _switchable = switchableAnalyzer;

        GameSession session;
        try
        {
            session = await MainWindowViewModel.RestoreOrStartAsync(store, dialogs, _defaultOptions);
        }
        catch (Exception exception)
        {
            await dialogs.ShowMessageAsync("恢复对局", "自动保存已损坏，将开始一盘新对局。");
            await store.ClearAsync(CancellationToken.None);
            await logger.WriteEngineAsync(new EngineLogEntry("", "", null, null, 0,
                exception.GetType().Name));
            session = GameSession.Start(_defaultOptions);
        }

        GameViewModel CreateGame(GameSession gameSession)
        {
            var coordinator = new AnalysisCoordinator(
                switchableAnalyzer,
                moveNumber => _analysisSettings.DurationForMove(moveNumber),
                new Progress<AnalysisResult>(_ => { }));
            var viewModel = new GameViewModel(gameSession, coordinator, store, dialogs, aiAvailable: false);
            viewModel.SetEngineStartupState(_engineState);
            return viewModel;
        }

        MainWindowViewModel? shell = null;
        var initial = CreateGame(session);
        shell = new MainWindowViewModel(
            initial,
            options =>
            {
                switchableAnalyzer.ResetSession();
                if (switchableAnalyzer.IsConfigured && _engineState == EngineStartupState.Unavailable)
                    _engineState = _selectedEngineState;
                var replacement = CreateGame(GameSession.Start(options));
                return replacement;
            },
            switchableAnalyzer);
        _shell = shell;
        var window = new MainWindow { DataContext = shell };
        window.NewGameRequested += async options =>
        {
            try { await shell.StartNewGameAsync(options); }
            catch (Exception exception)
            {
                await dialogs.ShowMessageAsync("新对局", $"创建新对局时发生错误：{exception.Message}");
            }
        };
        window.SettingsRequested += () => new SettingsViewModel(
            _analysisSettings,
            settings =>
            {
                _analysisSettings = settings;
                _ = _settingsStore.SaveAsync(settings);
            },
            RebenchmarkEngineAsync);
        MainWindow = window;
        window.Show();

        _ = Task.Run(() => InitializeEngineAsync(switchableAnalyzer, shell, logger));
    }

    private async Task InitializeEngineAsync(
        SwitchablePositionAnalyzer target,
        MainWindowViewModel shell,
        RollingFileLogger logger)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var engine = await BuildEngineAsync(cachedProfile: null, shell, logger, stopwatch);
            if (!target.TryConfigure(engine.Analyzer))
            {
                await engine.Analyzer.DisposeAsync();
                return;
            }

            _selectedEngineState = engine.CpuFallback
                ? EngineStartupState.CpuFallback
                : EngineStartupState.Ready;
            PublishEngineState(shell, _selectedEngineState);
            await logger.WriteEngineAsync(new EngineLogEntry(
                engine.Version, engine.Backend, 0, null, stopwatch.ElapsedMilliseconds, null));
        }
        catch (Exception exception)
        {
            if (target.MarkUnavailable(exception))
                PublishEngineState(shell, EngineStartupState.Unavailable);
            await logger.WriteEngineAsync(new EngineLogEntry(
                "", "", null, null, stopwatch.ElapsedMilliseconds,
                exception.GetType().Name));
        }
        finally
        {
            _engineInitialized.TrySetResult();
        }
    }

    private async Task<EngineSelection> RebenchmarkEngineAsync()
    {
        try
        {
            await _engineInitialized.Task;
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException("引擎初始化被取消，无法重新基准测试。");
        }

        var stopwatch = Stopwatch.StartNew();
        var engine = await BuildEngineAsync(cachedProfile: null, _shell, _logger, stopwatch, freshBenchmark: true);
        var switched = await _switchable.TrySwitchAsync(engine.Analyzer);
        if (!switched)
        {
            await engine.Analyzer.DisposeAsync();
            throw new InvalidOperationException("分析引擎已关闭，无法切换引擎。");
        }

        _selectedEngineState = engine.CpuFallback
            ? EngineStartupState.CpuFallback
            : EngineStartupState.Ready;
        PublishEngineState(_shell, _selectedEngineState);
        await _logger.WriteEngineAsync(new EngineLogEntry(
            engine.Version, engine.Backend, 0, null, stopwatch.ElapsedMilliseconds, null));
        return engine.Selection;
    }

    private async Task<BuiltEngine> BuildEngineAsync(
        EngineProfile? cachedProfile,
        MainWindowViewModel shell,
        RollingFileLogger logger,
        Stopwatch stopwatch,
        bool freshBenchmark = false)
    {
        var assetRoot = Path.Combine(AppContext.BaseDirectory, "assets", "katago");
        var manifest = await EngineManifest.LoadAsync(Path.Combine(assetRoot, "engine-manifest.json"));
        var selector = new BackendSelector(manifest, assetRoot);
        var selection = freshBenchmark
            ? await selector.SelectFreshAsync()
            : await selector.SelectAsync(cachedProfile);
        var version = selection.Candidate.KataGoVersion;
        var backend = selection.Candidate.Backend.ToString();
        var runtimeConfig = await AnalysisConfiguration.WriteRuntimeAsync(
            Path.Combine(assetRoot, selection.Candidate.Config), selection.Profile);

        Task<IPositionAnalyzer> CreateClient(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transport = new ProcessKataGoTransport(
                Path.Combine(assetRoot, selection.Candidate.Executable),
                Path.Combine(assetRoot, selection.Candidate.Model),
                runtimeConfig);
            return Task.FromResult<IPositionAnalyzer>(new KataGoAnalysisClient(transport));
        }

        var initial = await CreateClient(CancellationToken.None);
        var recovering = new RestartingPositionAnalyzer(
            initial,
            CreateClient,
            _ => shell.CurrentGame.SaveNowAsync(),
            _ => PublishEngineState(shell, EngineStartupState.Unavailable),
            (exception, failureNumber, requestId) => _ = logger.WriteEngineAsync(new EngineLogEntry(
                version, backend, GetExitCode(exception), requestId, stopwatch.ElapsedMilliseconds,
                exception.GetType().Name)));
        var cpuFallback = selection.Candidate.Backend is EngineBackend.Eigen or EngineBackend.EigenAvx2;
        return new BuiltEngine(selection, version, backend, cpuFallback, recovering);
    }

    private void PublishEngineState(MainWindowViewModel shell, EngineStartupState state)
    {
        _engineState = state;
        shell.CurrentGame.SetEngineStartupState(state);
    }

    private static int? GetExitCode(Exception exception) =>
        exception is KataGoProcessExitedException exited ? exited.ExitCode : null;

    private sealed record BuiltEngine(
        EngineSelection Selection,
        string Version,
        string Backend,
        bool CpuFallback,
        RestartingPositionAnalyzer Analyzer);
}
