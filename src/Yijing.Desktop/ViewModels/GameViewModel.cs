using System.Globalization;
using System.Windows.Input;
using Yijing.Application.Analysis;
using Yijing.Application.Games;
using Yijing.Application.Persistence;
using Yijing.Desktop.Services;
using Yijing.Domain.Board;

namespace Yijing.Desktop.ViewModels;

public enum EngineStartupState { Detecting, Ready, CpuFallback, Unavailable }

public sealed class GameViewModel : ObservableObject, IAsyncDisposable
{
    private readonly GameSession _session;
    private readonly AnalysisCoordinator _analysisCoordinator;
    private readonly IGameStore _gameStore;
    private readonly IDialogService _dialogs;
    private readonly SynchronizationContext? _uiContext;
    private readonly int _uiThreadId;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _lifecycleLock = new();
    private readonly List<CandidateMove> _candidates = [];
    private CancellationTokenSource? _analysisCancellation;
    private Task? _analysisTask;
    private bool _isAnalysisVisible = true;
    private bool _isInputEnabled;
    private bool _isOperationInProgress;
    private bool _isRetryAvailable;
    private bool _isShutdown;
    private Task? _shutdownTask;
    private long _analysisGeneration;
    private int? _activeAnalysisRevision;
    private string _winrateText = "胜率：--";
    private string _scoreLeadText = "目差：--";
    private string _turnText = "黑方行棋";
    private string _engineStatusText = "AI 准备完成";
    private IReadOnlyList<double>? _latestOwnership;
    private bool _isAiAvailable;

    public GameViewModel(
        GameSession session,
        AnalysisCoordinator analysisCoordinator,
        IGameStore gameStore,
        IDialogService dialogs,
        bool aiAvailable = true)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _analysisCoordinator = analysisCoordinator ?? throw new ArgumentNullException(nameof(analysisCoordinator));
        _gameStore = gameStore ?? throw new ArgumentNullException(nameof(gameStore));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _isAiAvailable = aiAvailable;
        _uiContext = SynchronizationContext.Current;
        _uiThreadId = Environment.CurrentManagedThreadId;

        PlayCommand = new AsyncRelayCommand(PlayAsync, parameter => parameter is BoardPoint && CanMutateBoard());
        UndoCommand = new AsyncRelayCommand(_ => UndoAsync(), _ => CanUndo());
        PassCommand = new AsyncRelayCommand(_ => PassAsync(), _ => CanMutateBoard());
        ResignCommand = new AsyncRelayCommand(_ => ResignAsync(), _ => CanResign());
        RetryAiCommand = new AsyncRelayCommand(_ => RetryAiAsync(), _ => CanRetryAi());
        UpdateBindings();
        if (ShouldAiPlay()) StartAiSearch();
    }

    public BoardState State => _session.State;
    public IReadOnlyList<CandidateMove> Candidates => _candidates.ToArray();
    public BoardPoint? LastMove
    {
        get
        {
            var last = _session.Moves.LastOrDefault();
            return last?.Move.Kind == MoveKind.Play ? last.Move.Point : null;
        }
    }
    public string ModeText => _session.Options.Mode == GameMode.HumanVsAi ? "人机对弈" : "本地双人";
    public string BoardSizeText => $"{_session.Options.BoardSize} 路";
    public string WinrateText => _winrateText;
    public string ScoreLeadText => _scoreLeadText;
    public string TurnText => _turnText;
    public string EngineStatusText => _engineStatusText;

    public bool IsAnalysisVisible
    {
        get => _isAnalysisVisible;
        set
        {
            if (!SetProperty(ref _isAnalysisVisible, value)) return;
            if (!value) ClearAnalysisDisplay();
        }
    }

    public bool IsInputEnabled
    {
        get => _isInputEnabled;
        private set
        {
            if (!SetProperty(ref _isInputEnabled, value)) return;
            NotifyCommands();
        }
    }

    public ICommand PlayCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand PassCommand { get; }
    public ICommand ResignCommand { get; }
    public ICommand RetryAiCommand { get; }

    public void SetEngineStartupState(EngineStartupState state)
    {
        if (!IsOnUiThread)
        {
            _uiContext!.Post(_ => SetEngineStartupState(state), null);
            return;
        }
        _isAiAvailable = state is EngineStartupState.Ready or EngineStartupState.CpuFallback;
        _engineStatusText = state switch
        {
            EngineStartupState.Detecting => "正在检测 AI",
            EngineStartupState.Ready => "AI 准备完成",
            EngineStartupState.CpuFallback => "GPU 不可用，已切换 CPU",
            EngineStartupState.Unavailable => "AI 暂不可用，可进行本地双人对局",
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };
        OnPropertyChanged(nameof(EngineStatusText));
        UpdateBindings(preserveEngineStatus: true);
        if (_isAiAvailable && ShouldAiPlay()) StartAiSearch();
    }

    /// <summary>
    /// Legacy progress delivery has no generation token and is intentionally ignored.
    /// Engine integration uses the per-search callback created by this view model.
    /// </summary>
    public void ReportAnalysis(AnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
    }

    public Task ShutdownAsync()
    {
        lock (_lifecycleLock)
        {
            return _shutdownTask ??= InvokeOnUiAsync(ShutdownCoreAsync);
        }
    }

    public ValueTask DisposeAsync() => new(ShutdownAsync());

    private async Task PlayAsync(object? parameter)
    {
        if (parameter is not BoardPoint point) return;
        if (!CanMutateBoard()) return;
        await ExecuteMutationAsync(() => PlayMoveAsync(Move.Play(point)));
    }

    private Task PassAsync()
    {
        if (!CanMutateBoard()) return Task.CompletedTask;
        return ExecuteMutationAsync(() => PlayMoveAsync(Move.Pass()));
    }

    private async Task PlayMoveAsync(Move move)
    {
        var result = _session.Play(move);
        if (!result.IsLegal) return;

        InvalidateAnalysisDisplay();
        await SaveAsync();
        await HandleAfterLegalMoveAsync();
    }

    private Task UndoAsync() => ExecuteMutationAsync(UndoCoreAsync);

    private async Task UndoCoreAsync()
    {
        await CancelAnalysisAsync();
        if (!_session.Undo()) return;

        InvalidateAnalysisDisplay();
        await SaveAsync();
        UpdateBindings();
        if (ShouldAiPlay()) StartAiSearch();
    }

    private async Task ResignAsync()
    {
        await ExecuteMutationAsync(async () =>
        {
            var confirmed = await _dialogs.ConfirmAsync("认输", "确定认输吗？本局将立即结束。");
            if (!confirmed || _session.Result is not null) return;

            await CancelAnalysisAsync();
            InvalidateAnalysisDisplay();
            var resigningColor = _session.Options.Mode == GameMode.HumanVsAi
                ? _session.Options.HumanColor!.Value
                : _session.State.NextPlayer;
            _session.Resign(resigningColor);
            await SaveAsync();
            UpdateBindings();
        });
    }

    private Task RetryAiAsync()
    {
        if (!CanRetryAi()) return Task.CompletedTask;
        return ExecuteMutationAsync(() =>
        {
            if (_isRetryAvailable && !_session.IsAiThinking && ShouldAiPlay()) StartAiSearch();
            return Task.CompletedTask;
        });
    }

    private async Task ExecuteMutationAsync(Func<Task> action)
    {
        await _operationGate.WaitAsync();
        try
        {
            if (_isShutdown) return;
            _isOperationInProgress = true;
            IsInputEnabled = false;
            NotifyCommands();
            await action();
        }
        catch (Exception exception)
        {
            await ShowOperationErrorAsync(exception);
        }
        finally
        {
            try
            {
                if (!_isShutdown)
                {
                    _isOperationInProgress = false;
                    UpdateBindings();
                }
            }
            finally
            {
                _operationGate.Release();
            }
        }
    }

    private void StartAiSearch()
    {
        if (_isShutdown || _analysisTask is { IsCompleted: false } || !ShouldAiPlay()) return;

        _session.SetAiThinking(true);
        _isRetryAvailable = false;
        IsInputEnabled = false;
        _engineStatusText = "AI 正在思考";
        OnPropertyChanged(nameof(EngineStatusText));
        NotifyCommands();

        var cancellation = new CancellationTokenSource();
        var generation = ++_analysisGeneration;
        var revision = _session.Revision;
        _analysisCancellation = cancellation;
        _activeAnalysisRevision = revision;
        ClearAnalysisDisplay();
        var progress = new CallbackProgress(result => ReportAnalysis(result, generation, revision));
        _analysisTask = RunAiSearchAsync(cancellation, generation, revision, progress);
    }

    private async Task RunAiSearchAsync(
        CancellationTokenSource cancellation,
        long generation,
        int revision,
        IProgress<AnalysisResult> progress)
    {
        try
        {
            var move = await _analysisCoordinator.FindAiMoveAsync(_session, progress, cancellation.Token);
            if (move is not null && !_isShutdown && !cancellation.IsCancellationRequested && _session.Result is null &&
                _session.Revision == revision)
            {
                var result = _session.Play(move.Value);
                if (result.IsLegal)
                {
                    InvalidateAnalysisDisplay();
                    await SaveAsync();
                    await HandleAfterLegalMoveAsync();
                }
            }
            else if (!_isShutdown && !cancellation.IsCancellationRequested && _session.Result is null && _session.Revision == revision)
            {
                _isRetryAvailable = true;
                _engineStatusText = "AI 未找到合法着法，请重试";
                OnPropertyChanged(nameof(EngineStatusText));
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!_isShutdown && !cancellation.IsCancellationRequested)
            {
                _isRetryAvailable = _isAiAvailable && ShouldAiPlay();
                _engineStatusText = _isAiAvailable
                    ? "AI 分析失败，请重试"
                    : "AI 暂不可用，可进行本地双人对局";
                OnPropertyChanged(nameof(EngineStatusText));
                if (_isAiAvailable) await _dialogs.ShowMessageAsync("AI", exception.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(_analysisCancellation, cancellation))
            {
                _analysisCancellation = null;
                _activeAnalysisRevision = null;
                _session.SetAiThinking(false);
                if (!_isShutdown) UpdateBindings();
            }
            cancellation.Dispose();
        }
    }

    private async Task CancelAnalysisAsync()
    {
        var cancellation = _analysisCancellation;
        var task = _analysisTask;
        if (cancellation is null || task is null) return;

        cancellation.Cancel();
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ReportAnalysis(AnalysisResult result, long generation, int? requestRevision)
    {
        if (!IsOnUiThread)
        {
            _uiContext!.Post(_ => ReportAnalysis(result, generation, requestRevision), null);
            return;
        }

        if (!IsAnalysisVisible || generation != _analysisGeneration || requestRevision != _activeAnalysisRevision ||
            requestRevision != _session.Revision || !_session.IsAiThinking)
        {
            return;
        }

        _candidates.Clear();
        _candidates.AddRange(result.Candidates.Take(3));
        if (result.Ownership is { Count: > 0 }) _latestOwnership = result.Ownership.ToArray();
        _winrateText = $"胜率：{result.RootWinrate.ToString("P1", CultureInfo.CurrentCulture)}";
        _scoreLeadText = $"目差：{result.RootScoreLead.ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture)}";
        OnPropertyChanged(nameof(Candidates));
        OnPropertyChanged(nameof(WinrateText));
        OnPropertyChanged(nameof(ScoreLeadText));
    }

    private bool IsOnUiThread => _uiContext is null || Environment.CurrentManagedThreadId == _uiThreadId;

    private bool ShouldAiPlay() => _isAiAvailable && _session.Result is null
        && _session.Options.Mode == GameMode.HumanVsAi
        && _session.Options.HumanColor is { } humanColor
        && _session.State.NextPlayer != humanColor;

    private bool CanMutateBoard() => !_isShutdown && !_isOperationInProgress && IsInputEnabled && _session.Result is null;

    private bool CanUndo() => !_isShutdown && !_isOperationInProgress && _session.Result is null &&
        (_session.Options.Mode == GameMode.LocalTwoPlayer
            ? _session.Moves.Count > 0
            : _session.Options.HumanColor is { } humanColor && _session.Moves.Any(move => move.Color == humanColor));

    private bool CanResign() => !_isShutdown && !_isOperationInProgress && _session.Result is null;

    private bool CanRetryAi() => !_isShutdown && !_isOperationInProgress && _isRetryAvailable && !_session.IsAiThinking && ShouldAiPlay();

    private async Task SaveAsync() => await _gameStore.SaveAsync(
        new GameSnapshotDto(_session.Options, _session.Moves.ToArray(), _session.Result, _session.Revision),
        CancellationToken.None);

    public Task SaveNowAsync() => SaveAsync();

    private async Task HandleAfterLegalMoveAsync()
    {
        UpdateBindings();
        if (_session.State.HasTwoConsecutivePasses)
        {
            var scoring = new ScoringViewModel(_session.State, _session.Options.Komi, _latestOwnership);
            var outcome = await _dialogs.ShowScoringAsync(scoring);
            if (outcome == ScoringDialogOutcome.ConfirmScore)
                _session.FinishByScore(scoring.Score.Winner, scoring.Score.Margin);
            else
                _session.ResumeAfterScoringDispute();
            await SaveAsync();
            UpdateBindings();
        }

        if (ShouldAiPlay()) StartAiSearch();
    }

    private void InvalidateAnalysisDisplay()
    {
        _analysisGeneration++;
        _isRetryAvailable = false;
        ClearAnalysisDisplay();
    }

    private void ClearAnalysisDisplay()
    {
        _candidates.Clear();
        _winrateText = "胜率：--";
        _scoreLeadText = "目差：--";
        OnPropertyChanged(nameof(Candidates));
        OnPropertyChanged(nameof(WinrateText));
        OnPropertyChanged(nameof(ScoreLeadText));
    }

    private async Task ShowOperationErrorAsync(Exception exception)
    {
        _engineStatusText = "操作失败";
        OnPropertyChanged(nameof(EngineStatusText));
        await _dialogs.ShowMessageAsync("错误", exception.Message);
    }

    private void UpdateBindings(bool preserveEngineStatus = false)
    {
        var isHumanTurn = _session.Options.Mode == GameMode.LocalTwoPlayer ||
            _session.Options.HumanColor == _session.State.NextPlayer;
        IsInputEnabled = !_isOperationInProgress && _session.Result is null && !_session.IsAiThinking && isHumanTurn;
        _turnText = _session.Result is null
            ? $"{(_session.State.NextPlayer == StoneColor.Black ? "黑" : "白")}方行棋"
            : $"{(_session.Result.Winner == StoneColor.Black ? "黑" : "白")}方胜（{_session.Result.SgfValue}）";
        if (!preserveEngineStatus)
        {
            if (_session.Result is not null) _engineStatusText = "对局已结束";
            else if (_session.IsAiThinking) _engineStatusText = "AI 正在思考";
            else if (!_isRetryAvailable && _isAiAvailable) _engineStatusText = "AI 准备完成";
        }
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(LastMove));
        OnPropertyChanged(nameof(TurnText));
        OnPropertyChanged(nameof(EngineStatusText));
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        ((AsyncRelayCommand)PlayCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)UndoCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)PassCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)ResignCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)RetryAiCommand).NotifyCanExecuteChanged();
    }

    private sealed class CallbackProgress(Action<AnalysisResult> callback) : IProgress<AnalysisResult>
    {
        public void Report(AnalysisResult value) => callback(value);
    }

    private async Task ShutdownCoreAsync()
    {
        _isShutdown = true;
        _isOperationInProgress = true;
        IsInputEnabled = false;
        _isRetryAvailable = false;
        NotifyCommands();

        await _operationGate.WaitAsync();
        try
        {
            InvalidateAnalysisDisplay();
            await CancelAnalysisAsync();
            _session.SetAiThinking(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private Task InvokeOnUiAsync(Func<Task> action)
    {
        if (IsOnUiThread) return action();

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _uiContext!.Post(async _ =>
        {
            try
            {
                await action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }, null);
        return completion.Task;
    }
}
