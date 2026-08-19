using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Threading;
using Yijing.Application.Analysis;
using Yijing.Application.Games;
using Yijing.Application.Persistence;
using Yijing.Desktop.Services;
using Yijing.Desktop.ViewModels;
using Yijing.Domain.Board;
using Yijing.Domain.Scoring;

namespace Yijing.Desktop.Tests;

public sealed class GameViewModelTests : IAsyncLifetime
{
    private readonly List<GameViewModel> _viewModels = [];

    [Fact]
    public async Task Legal_board_click_updates_state_increments_revision_and_saves_once()
    {
        var store = new RecordingStore();
        var session = GameSession.Start(new GameOptions(GameMode.LocalTwoPlayer, 9, null, 7.5));
        var viewModel = CreateViewModel(session, store);

        await ((AsyncRelayCommand)viewModel.PlayCommand).ExecuteAsync(new BoardPoint(0, 0));

        Assert.Equal(StoneColor.Black, viewModel.State.At(new BoardPoint(0, 0)));
        Assert.Equal(1, session.Revision);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task Occupied_click_does_not_save()
    {
        var store = new RecordingStore();
        var session = GameSession.Start(new GameOptions(GameMode.LocalTwoPlayer, 9, null, 7.5));
        var viewModel = CreateViewModel(session, store);
        await ((AsyncRelayCommand)viewModel.PlayCommand).ExecuteAsync(new BoardPoint(0, 0));

        await ((AsyncRelayCommand)viewModel.PlayCommand).ExecuteAsync(new BoardPoint(0, 0));

        Assert.Equal(1, store.SaveCount);
        Assert.Equal(1, session.Revision);
    }

    [Fact]
    public async Task Undo_cancels_analysis_undoes_session_and_saves()
    {
        var analyzer = new BlockingAnalyzer();
        var store = new RecordingStore();
        var session = GameSession.Start(new GameOptions(GameMode.HumanVsAi, 9, StoneColor.Black, 7.5));
        var viewModel = CreateViewModel(session, store, analyzer);
        await ((AsyncRelayCommand)viewModel.PlayCommand).ExecuteAsync(new BoardPoint(0, 0));
        await analyzer.WaitForAnalysisAsync();

        await ((AsyncRelayCommand)viewModel.UndoCommand).ExecuteAsync(null);

        Assert.Equal(0, session.Revision % 2);
        Assert.Empty(session.Moves);
        Assert.Equal(2, store.SaveCount);
        Assert.True(analyzer.TerminateCount > 0);
    }

    [Fact]
    public async Task Resign_confirmation_stores_white_resignation_win_when_human_is_black()
    {
        var store = new RecordingStore();
        var dialogs = new RecordingDialogs { ConfirmResult = true };
        var session = GameSession.Start(new GameOptions(GameMode.HumanVsAi, 9, StoneColor.Black, 7.5));
        var viewModel = CreateViewModel(session, store, dialogs: dialogs);

        await ((AsyncRelayCommand)viewModel.ResignCommand).ExecuteAsync(null);

        Assert.Equal("确定认输吗？本局将立即结束。", dialogs.LastMessage);
        Assert.Equal("W+R", session.Result!.SgfValue);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task Hiding_analysis_clears_displayed_candidates_without_stopping_game()
    {
        var analyzer = new StreamingAnalyzer();
        var store = new RecordingStore();
        var session = GameSession.Start(new GameOptions(GameMode.HumanVsAi, 9, StoneColor.Black, 7.5));
        var viewModel = CreateViewModel(session, store, analyzer);
        await ((AsyncRelayCommand)viewModel.PlayCommand).ExecuteAsync(new BoardPoint(0, 0));
        await analyzer.WaitForProgressAsync();

        Assert.NotEmpty(viewModel.Candidates);
        viewModel.IsAnalysisVisible = false;

        Assert.Empty(viewModel.Candidates);
        Assert.Null(session.Result);
        Assert.True(session.IsAiThinking);
    }

    [Fact]
    public async Task Human_white_cannot_undo_after_the_ai_has_played_its_opening()
    {
        var session = GameSession.Start(new GameOptions(GameMode.HumanVsAi, 9, StoneColor.White, 7.5));
        var viewModel = CreateViewModel(session, new RecordingStore(), new OpeningMoveAnalyzer());

        await WaitUntilAsync(() => session.Moves.Count == 1 && !session.IsAiThinking);

        Assert.Equal(StoneColor.Black, session.Moves[0].Color);
        Assert.False(viewModel.UndoCommand.CanExecute(null));
    }
    [Fact]
    public async Task Local_two_player_can_undo_its_single_most_recent_move()
    {
        var session = GameSession.Start(new GameOptions(GameMode.LocalTwoPlayer, 9, null, 7.5));
        var viewModel = CreateViewModel(session, new RecordingStore());
        await ((AsyncRelayCommand)viewModel.PlayCommand).ExecuteAsync(new BoardPoint(0, 0));

        Assert.True(viewModel.UndoCommand.CanExecute(null));
        await ((AsyncRelayCommand)viewModel.UndoCommand).ExecuteAsync(null);
        Assert.Empty(session.Moves);
    }

    [Fact]
    public async Task Pass_clears_the_latest_stone_marker()
    {
        var session = GameSession.Start(new GameOptions(GameMode.LocalTwoPlayer, 9, null, 7.5));
        var viewModel = CreateViewModel(session, new RecordingStore());
        var playedPoint = new BoardPoint(4, 4);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) => changedProperties.Add(eventArgs.PropertyName);

        await ((AsyncRelayCommand)viewModel.PlayCommand).ExecuteAsync(playedPoint);
        Assert.Equal(playedPoint, viewModel.LastMove);
        changedProperties.Clear();

        await ((AsyncRelayCommand)viewModel.PassCommand).ExecuteAsync(null);

        Assert.Equal(MoveKind.Pass, session.Moves[^1].Move.Kind);
        Assert.Null(viewModel.LastMove);
        Assert.Contains(nameof(GameViewModel.LastMove), changedProperties);
    }

    [Fact]
    public async Task Two_consecutive_passes_open_scoring_and_confirm_the_actual_margin()
    {
        var store = new RecordingStore();
        var dialogs = new RecordingDialogs { ScoringOutcome = ScoringDialogOutcome.ConfirmScore };
        var session = GameSession.Start(new GameOptions(GameMode.LocalTwoPlayer, 9, null, 7.5));
        var viewModel = CreateViewModel(session, store, dialogs: dialogs);

        await ((AsyncRelayCommand)viewModel.PassCommand).ExecuteAsync(null);
        Assert.Null(dialogs.LastScoring);
        await ((AsyncRelayCommand)viewModel.PassCommand).ExecuteAsync(null);

        Assert.NotNull(dialogs.LastScoring);
        Assert.NotNull(session.Result);
        Assert.Equal(GameEndReason.Score, session.Result.Reason);
        Assert.Equal(dialogs.LastScoring!.Score.Margin, session.Result.Margin);
        Assert.Equal("W+7.5", session.Result.SgfValue);
    }

    [Fact]
    public async Task Continue_after_scoring_clears_pass_count_without_deleting_moves()
    {
        var dialogs = new RecordingDialogs { ScoringOutcome = ScoringDialogOutcome.ContinueGame };
        var session = GameSession.Start(new GameOptions(GameMode.LocalTwoPlayer, 9, null, 7.5));
        var viewModel = CreateViewModel(session, new RecordingStore(), dialogs: dialogs);

        await ((AsyncRelayCommand)viewModel.PassCommand).ExecuteAsync(null);
        await ((AsyncRelayCommand)viewModel.PassCommand).ExecuteAsync(null);

        Assert.Equal(2, session.Moves.Count);
        Assert.Equal(0, session.State.ConsecutivePasses);
        Assert.Null(session.Result);
        Assert.True(viewModel.IsInputEnabled);
    }

    [Fact]
    public void Scoring_toggle_changes_the_whole_connected_group_and_recomputes_chinese_area()
    {
        var a = new BoardPoint(0, 0);
        var b = new BoardPoint(0, 1);
        var state = BoardState.FromSetup(9,
        [
            (a, StoneColor.Black),
            (b, StoneColor.Black),
            (new BoardPoint(8, 8), StoneColor.White),
        ], StoneColor.Black);
        var scoring = new ScoringViewModel(state, 7.5, ownership: null);
        var before = scoring.Score;

        scoring.ToggleDeadGroup(a);

        Assert.Contains(a, scoring.DeadStones);
        Assert.Contains(b, scoring.DeadStones);
        Assert.NotEqual(before, scoring.Score);
        scoring.ToggleDeadGroup(b);
        Assert.Empty(scoring.DeadStones);
    }

    [Fact]
    public void Scoring_suggestions_require_high_confidence_and_opposing_predicted_owner()
    {
        var state = BoardState.FromSetup(2,
        [
            (new BoardPoint(0, 0), StoneColor.Black),
            (new BoardPoint(1, 1), StoneColor.White),
        ], StoneColor.Black);
        // KataGo ownership: positive is black, negative is white.
        var scoring = new ScoringViewModel(state, 7.5, [-.96, -.94, .1, .99]);

        Assert.Contains(new BoardPoint(0, 0), scoring.DeadStones);
        Assert.Contains(new BoardPoint(1, 1), scoring.DeadStones);
    }

    [Fact]
    public void Autosave_restore_replays_all_moves_through_rules_and_rejects_tampered_history()
    {
        var options = new GameOptions(GameMode.LocalTwoPlayer, 9, null, 7.5);
        var moves = new[]
        {
            new PlayedMove(StoneColor.Black, Move.Play(new BoardPoint(0, 0))),
            new PlayedMove(StoneColor.White, Move.Play(new BoardPoint(1, 1))),
        };
        var restored = MainWindowViewModel.ReplaySnapshot(new GameSnapshotDto(options, moves, null, 2));

        Assert.Equal(StoneColor.Black, restored.State.At(new BoardPoint(0, 0)));
        Assert.Equal(StoneColor.White, restored.State.At(new BoardPoint(1, 1)));
        var tampered = moves.Append(new PlayedMove(StoneColor.White, Move.Play(new BoardPoint(2, 2)))).ToArray();
        Assert.Throws<InvalidDataException>(() =>
            MainWindowViewModel.ReplaySnapshot(new GameSnapshotDto(options, tampered, null, 3)));
    }

    [Fact]
    public async Task Startup_autosave_asks_the_exact_restore_question_before_replaying()
    {
        var options = new GameOptions(GameMode.LocalTwoPlayer, 9, null, 7.5);
        var snapshot = new GameSnapshotDto(options,
        [
            new PlayedMove(StoneColor.Black, Move.Play(new BoardPoint(3, 3))),
        ], null, 1);
        var store = new RecordingStore { SnapshotToLoad = snapshot };
        var dialogs = new RecordingDialogs { ConfirmResult = true };

        var restored = await MainWindowViewModel.RestoreOrStartAsync(store, dialogs, options);

        Assert.Equal("恢复上次未完成的对局吗？", dialogs.LastMessage);
        Assert.Equal(StoneColor.Black, restored.State.At(new BoardPoint(3, 3)));
    }

    [Fact]
    public async Task Declining_startup_restore_discards_the_stale_autosave()
    {
        var options = new GameOptions(GameMode.LocalTwoPlayer, 9, null, 7.5);
        var store = new RecordingStore
        {
            SnapshotToLoad = new GameSnapshotDto(options,
                [new PlayedMove(StoneColor.Black, Move.Pass())], null, 1),
        };
        var dialogs = new RecordingDialogs { ConfirmResult = false };

        var session = await MainWindowViewModel.RestoreOrStartAsync(store, dialogs, options);

        Assert.Empty(session.Moves);
        Assert.Equal(1, store.ClearCount);
    }

    [Theory]
    [InlineData(EngineStartupState.Detecting, "正在检测 AI")]
    [InlineData(EngineStartupState.Ready, "AI 准备完成")]
    [InlineData(EngineStartupState.CpuFallback, "GPU 不可用，已切换 CPU")]
    [InlineData(EngineStartupState.Unavailable, "AI 暂不可用，可进行本地双人对局")]
    public void Engine_startup_states_use_the_four_required_messages(
        EngineStartupState state,
        string expected)
    {
        var session = GameSession.Start(new GameOptions(GameMode.LocalTwoPlayer, 9, null, 7.5));
        var viewModel = CreateViewModel(session, new RecordingStore());

        viewModel.SetEngineStartupState(state);

        Assert.Equal(expected, viewModel.EngineStatusText);
    }

    [Fact]
    public async Task Engine_state_post_from_worker_is_applied_once_on_the_captured_dispatcher_thread()
    {
        var ready = new TaskCompletionSource<GameViewModel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var session = GameSession.Start(new GameOptions(GameMode.LocalTwoPlayer, 9, null, 7.5));
            var analyzer = new AnalysisCoordinator(new BlockingAnalyzer(), TimeSpan.FromSeconds(30),
                new SynchronousProgress(_ => { }));
            var viewModel = new GameViewModel(session, analyzer, new RecordingStore(), new RecordingDialogs());
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(GameViewModel.EngineStatusText) &&
                    viewModel.EngineStatusText == "AI 暂不可用，可进行本地双人对局")
                {
                    updated.TrySetResult();
                    Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            };
            ready.SetResult(viewModel);
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        var target = await ready.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await Task.Run(() => target.SetEngineStartupState(EngineStartupState.Unavailable));

        await updated.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(thread.Join(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Concurrent_play_and_pass_do_not_interleave_while_save_is_pending()
    {
        var store = new BlockingStore();
        var session = GameSession.Start(new GameOptions(GameMode.LocalTwoPlayer, 9, null, 7.5));
        var viewModel = CreateViewModel(session, store);

        var play = ((AsyncRelayCommand)viewModel.PlayCommand).ExecuteAsync(new BoardPoint(0, 0));
        await store.WaitForSaveAsync();
        var pass = ((AsyncRelayCommand)viewModel.PassCommand).ExecuteAsync(null);
        store.Release();
        await Task.WhenAll(play, pass);

        Assert.Single(session.Moves);
        Assert.Equal(MoveKind.Play, session.Moves[0].Move.Kind);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task Null_ai_result_keeps_human_input_disabled_and_exposes_retry_command()
    {
        var session = GameSession.Start(new GameOptions(GameMode.HumanVsAi, 9, StoneColor.Black, 7.5));
        var viewModel = CreateViewModel(session, new RecordingStore(), new NullResultAnalyzer());

        await ((AsyncRelayCommand)viewModel.PlayCommand).ExecuteAsync(new BoardPoint(0, 0));
        await WaitUntilAsync(() => viewModel.EngineStatusText.Contains("未找到合法着法", StringComparison.Ordinal));

        Assert.False(viewModel.IsInputEnabled);
        Assert.True(viewModel.RetryAiCommand.CanExecute(null));
    }

    [Fact]
    public async Task Hiding_then_showing_analysis_accepts_later_updates_from_the_same_search()
    {
        var analyzer = new PausableStreamingAnalyzer();
        var session = GameSession.Start(new GameOptions(GameMode.HumanVsAi, 9, StoneColor.Black, 7.5));
        var viewModel = CreateViewModel(session, new RecordingStore(), analyzer);
        await ((AsyncRelayCommand)viewModel.PlayCommand).ExecuteAsync(new BoardPoint(0, 0));
        await analyzer.WaitForAnalysisAsync();

        analyzer.Publish("A1");
        await Task.Delay(100);
        Assert.True(viewModel.Candidates.SingleOrDefault()?.Move == "A1", $"status={viewModel.EngineStatusText}; thinking={session.IsAiThinking}; revision={session.Revision}; count={viewModel.Candidates.Count}");
        viewModel.IsAnalysisVisible = false;
        Assert.Empty(viewModel.Candidates);
        Assert.Equal("胜率：--", viewModel.WinrateText);
        viewModel.IsAnalysisVisible = true;

        analyzer.Publish("B1");
        await WaitUntilAsync(() => viewModel.Candidates.SingleOrDefault()?.Move == "B1");
    }

    [Fact]
    public async Task Retry_starts_a_new_ai_search_and_undo_clears_retry_state()
    {
        var analyzer = new CountingNullAnalyzer();
        var session = GameSession.Start(new GameOptions(GameMode.HumanVsAi, 9, StoneColor.Black, 7.5));
        var viewModel = CreateViewModel(session, new RecordingStore(), analyzer);
        await ((AsyncRelayCommand)viewModel.PlayCommand).ExecuteAsync(new BoardPoint(0, 0));
        await WaitUntilAsync(() => analyzer.CallCount == 1 && viewModel.RetryAiCommand.CanExecute(null));

        await ((AsyncRelayCommand)viewModel.RetryAiCommand).ExecuteAsync(null);
        await WaitUntilAsync(() => analyzer.CallCount == 2 && viewModel.RetryAiCommand.CanExecute(null));
        await ((AsyncRelayCommand)viewModel.UndoCommand).ExecuteAsync(null);

        Assert.False(viewModel.RetryAiCommand.CanExecute(null));
        Assert.Equal("AI 准备完成", viewModel.EngineStatusText);
    }

    [Fact]
    public async Task Dispose_cancels_analysis_and_prevents_later_autosave_or_commands()
    {
        var analyzer = new LateResultAnalyzer();
        var store = new RecordingStore();
        var session = GameSession.Start(new GameOptions(GameMode.HumanVsAi, 9, StoneColor.Black, 7.5));
        var viewModel = CreateViewModel(session, store, analyzer);
        await ((AsyncRelayCommand)viewModel.PlayCommand).ExecuteAsync(new BoardPoint(0, 0));
        await analyzer.WaitForAnalysisAsync();
        var savesBeforeDisposal = store.SaveCount;

        await viewModel.DisposeAsync();
        var replacement = CreateViewModel(GameSession.Start(new GameOptions(GameMode.LocalTwoPlayer, 9, null, 7.5)), store);
        await ((AsyncRelayCommand)replacement.PlayCommand).ExecuteAsync(new BoardPoint(1, 1));
        var savesAfterReplacement = store.SaveCount;
        await Task.Delay(50);
        await ((AsyncRelayCommand)viewModel.PlayCommand).ExecuteAsync(new BoardPoint(2, 2));

        Assert.Equal(savesBeforeDisposal + 1, savesAfterReplacement);
        Assert.Equal(savesAfterReplacement, store.SaveCount);
        Assert.Single(session.Moves);
    }

    [Fact]
    public async Task Notification_exception_does_not_leave_operation_gate_held()
    {
        var session = GameSession.Start(new GameOptions(GameMode.LocalTwoPlayer, 9, null, 7.5));
        var viewModel = CreateViewModel(session, new RecordingStore());
        PropertyChangedEventHandler throwing = (_, _) => throw new InvalidOperationException("通知故障");
        viewModel.PropertyChanged += throwing;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ((AsyncRelayCommand)viewModel.PlayCommand).ExecuteAsync(new BoardPoint(0, 0)));
        viewModel.PropertyChanged -= throwing;

        await ((AsyncRelayCommand)viewModel.PlayCommand).ExecuteAsync(new BoardPoint(0, 0));
        Assert.Single(session.Moves);
    }
    [Fact]
    public async Task Can_execute_notification_exception_does_not_leave_operation_gate_held()
    {
        var session = GameSession.Start(new GameOptions(GameMode.LocalTwoPlayer, 9, null, 7.5));
        var viewModel = CreateViewModel(session, new RecordingStore());
        var command = (AsyncRelayCommand)viewModel.PlayCommand;
        EventHandler throwing = (_, _) => throw new InvalidOperationException("命令通知故障");
        command.CanExecuteChanged += throwing;

        await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteAsync(new BoardPoint(0, 0)));
        command.CanExecuteChanged -= throwing;

        await command.ExecuteAsync(new BoardPoint(0, 0));
        Assert.Single(session.Moves);
    }
    [Fact]
    public async Task Stale_analysis_progress_is_ignored_after_a_move_changes_the_revision()
    {
        var session = GameSession.Start(new GameOptions(GameMode.LocalTwoPlayer, 9, null, 7.5));
        var viewModel = CreateViewModel(session, new RecordingStore());
        var stale = new AnalysisResult("game-0-stale", false, [new CandidateMove("A1", .5, 0, 1)], .5, 0);

        await ((AsyncRelayCommand)viewModel.PlayCommand).ExecuteAsync(new BoardPoint(0, 0));
        viewModel.ReportAnalysis(stale);

        Assert.Empty(viewModel.Candidates);
    }

    [Fact]
    public async Task Async_command_routes_exception_without_leaking_from_i_command_execute()
    {
        var command = new AsyncRelayCommand(_ => Task.FromException(new InvalidOperationException("故障")));
        Exception? captured = null;
        command.ExecutionFailed += (_, exception) => captured = exception;

        command.Execute(null);
        await WaitUntilAsync(() => captured is not null);

        Assert.IsType<InvalidOperationException>(captured);
    }
    [Fact]
    public void New_game_options_restrict_sizes_hide_local_color_and_keep_chinese_komi()
    {
        var viewModel = new NewGameViewModel { Mode = GameMode.LocalTwoPlayer, BoardSize = 13 };

        Assert.Equal([19, 13, 9], NewGameViewModel.AvailableBoardSizes);
        Assert.False(viewModel.IsColorChoiceVisible);
        var options = viewModel.CreateOptions();
        Assert.Equal(7.5, options.Komi);
        Assert.Null(options.HumanColor);
        Assert.Throws<ArgumentOutOfRangeException>(() => viewModel.BoardSize = 10);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var viewModel in _viewModels)
            await viewModel.DisposeAsync();
    }
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(2))
                throw new TimeoutException("等待异步状态超时。");
            await Task.Delay(10);
        }
    }
    private GameViewModel CreateViewModel(
        GameSession session,
        IGameStore store,
        IPositionAnalyzer? analyzer = null,
        RecordingDialogs? dialogs = null)
    {
        GameViewModel? viewModel = null;
        var progress = new SynchronousProgress(result => viewModel!.ReportAnalysis(result));
        var coordinator = new AnalysisCoordinator(analyzer ?? new BlockingAnalyzer(), TimeSpan.FromSeconds(30), progress);
        var context = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            viewModel = new GameViewModel(session, coordinator, store, dialogs ?? new RecordingDialogs());
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(context);
        }
        _viewModels.Add(viewModel);
        return viewModel;
    }
    private sealed class SynchronousProgress(Action<AnalysisResult> report) : IProgress<AnalysisResult>
    {
        public void Report(AnalysisResult value) => report(value);
    }
    private sealed class BlockingStore : IGameStore
    {
        private readonly TaskCompletionSource<bool> _saveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int SaveCount { get; private set; }

        public async Task SaveAsync(GameSnapshotDto snapshot, CancellationToken cancellationToken)
        {
            SaveCount++;
            _saveStarted.TrySetResult(true);
            await _release.Task.WaitAsync(cancellationToken);
        }

        public Task<GameSnapshotDto?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult<GameSnapshotDto?>(null);
        public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WaitForSaveAsync() => _saveStarted.Task;
        public void Release() => _release.TrySetResult(true);
    }

    private sealed class RecordingStore : IGameStore
    {
        public int SaveCount { get; private set; }
        public GameSnapshotDto? LastSnapshot { get; private set; }
        public GameSnapshotDto? SnapshotToLoad { get; init; }
        public int ClearCount { get; private set; }

        public Task SaveAsync(GameSnapshotDto snapshot, CancellationToken cancellationToken)
        {
            SaveCount++;
            LastSnapshot = snapshot;
            return Task.CompletedTask;
        }

        public Task<GameSnapshotDto?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(SnapshotToLoad);
        public Task ClearAsync(CancellationToken cancellationToken)
        {
            ClearCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDialogs : IDialogService
    {
        public bool ConfirmResult { get; set; }
        public ScoringDialogOutcome ScoringOutcome { get; set; } = ScoringDialogOutcome.ContinueGame;
        public ScoringViewModel? LastScoring { get; private set; }
        public string? LastMessage { get; private set; }
        public Task<bool> ConfirmAsync(string title, string message)
        {
            LastMessage = message;
            return Task.FromResult(ConfirmResult);
        }

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Task<ScoringDialogOutcome> ShowScoringAsync(
            ScoringViewModel scoring,
            CancellationToken cancellationToken = default)
        {
            LastScoring = scoring;
            return Task.FromResult(ScoringOutcome);
        }
    }

    private sealed class BlockingAnalyzer : IPositionAnalyzer
    {
        private readonly TaskCompletionSource<bool> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _terminated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int TerminateCount { get; private set; }

        public async IAsyncEnumerable<AnalysisResult> AnalyzeAsync(AnalysisPosition position, string requestId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _started.TrySetResult(true);
            await _terminated.Task.WaitAsync(cancellationToken);
            yield break;
        }

        public Task TerminateAsync(string requestId, CancellationToken cancellationToken)
        {
            TerminateCount++;
            _terminated.TrySetResult(true);
            return Task.CompletedTask;
        }

        public Task WaitForAnalysisAsync() => _started.Task;
    }

    private sealed class OpeningMoveAnalyzer : IPositionAnalyzer
    {
        public async IAsyncEnumerable<AnalysisResult> AnalyzeAsync(AnalysisPosition position, string requestId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new AnalysisResult(requestId, true, [new CandidateMove("A1", 0.5, 0, 1)], 0.5, 0);
            await Task.CompletedTask;
        }

        public Task TerminateAsync(string requestId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CountingNullAnalyzer : IPositionAnalyzer
    {
        public int CallCount { get; private set; }

        public async IAsyncEnumerable<AnalysisResult> AnalyzeAsync(AnalysisPosition position, string requestId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            CallCount++;
            yield return new AnalysisResult(requestId, true, [], 0.5, 0);
            await Task.CompletedTask;
        }

        public Task TerminateAsync(string requestId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class LateResultAnalyzer : IPositionAnalyzer
    {
        private readonly TaskCompletionSource<bool> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _finish = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<AnalysisResult> AnalyzeAsync(AnalysisPosition position, string requestId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _started.TrySetResult(true);
            await _finish.Task;
            yield return new AnalysisResult(requestId, true, [new CandidateMove("A1", 0.5, 0, 1)], 0.5, 0);
        }

        public Task TerminateAsync(string requestId, CancellationToken cancellationToken)
        {
            _finish.TrySetResult(true);
            return Task.CompletedTask;
        }

        public Task WaitForAnalysisAsync() => _started.Task;
    }

    private sealed class PausableStreamingAnalyzer : IPositionAnalyzer
    {
        private readonly System.Threading.Channels.Channel<string> _moves = System.Threading.Channels.Channel.CreateUnbounded<string>();
        private readonly TaskCompletionSource<bool> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private string? _requestId;

        public async IAsyncEnumerable<AnalysisResult> AnalyzeAsync(AnalysisPosition position, string requestId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _requestId = requestId;
            _started.TrySetResult(true);
            await foreach (var move in _moves.Reader.ReadAllAsync(cancellationToken))
                yield return new AnalysisResult(requestId, false, [new CandidateMove(move, 0.5, 0, 1)], 0.5, 0);
        }

        public Task TerminateAsync(string requestId, CancellationToken cancellationToken)
        {
            _moves.Writer.TryComplete();
            return Task.CompletedTask;
        }

        public Task WaitForAnalysisAsync() => _started.Task;
        public void Publish(string move)
        {
            Assert.NotNull(_requestId);
            _moves.Writer.TryWrite(move);
        }
    }
    private sealed class NullResultAnalyzer : IPositionAnalyzer
    {
        public async IAsyncEnumerable<AnalysisResult> AnalyzeAsync(AnalysisPosition position, string requestId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new AnalysisResult(requestId, true, [], 0.5, 0.0);
            await Task.CompletedTask;
        }

        public Task TerminateAsync(string requestId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StreamingAnalyzer : IPositionAnalyzer
    {
        private readonly TaskCompletionSource<bool> _progress = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _terminated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async IAsyncEnumerable<AnalysisResult> AnalyzeAsync(AnalysisPosition position, string requestId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new AnalysisResult(requestId, false, [new CandidateMove("A1", 0.5, 0.0, 10)], 0.5, 0.0);
            _progress.TrySetResult(true);
            await _terminated.Task.WaitAsync(cancellationToken);
        }

        public Task TerminateAsync(string requestId, CancellationToken cancellationToken)
        {
            _terminated.TrySetResult(true);
            return Task.CompletedTask;
        }
        public Task WaitForProgressAsync() => _progress.Task;
    }
}
