using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Yijing.Application.Analysis;
using Yijing.Application.Games;
using Yijing.Domain.Board;

namespace Yijing.Infrastructure.KataGo;

public sealed class KataGoAnalysisClient : IPositionAnalyzer, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IKataGoTransport _transport;
    private readonly ConcurrentDictionary<string, RequestState> _requests = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _terminateIntents = new(StringComparer.Ordinal);
    private readonly object _lifecycleGate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _readLoop;
    private Exception? _terminalFailure;
    private int _disposed;

    public KataGoAnalysisClient(IKataGoTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
        _readLoop = Task.Run(ReadLoopAsync);
    }

    public async IAsyncEnumerable<AnalysisResult> AnalyzeAsync(
        AnalysisPosition position,
        string requestId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        cancellationToken.ThrowIfCancellationRequested();

        var state = new RequestState();
        var terminatedBeforeRegistration = false;
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_terminalFailure is not null)
            {
                ExceptionDispatchInfo.Capture(_terminalFailure).Throw();
                throw new InvalidOperationException("Unreachable.");
            }

            if (_terminateIntents.TryRemove(requestId, out _))
            {
                terminatedBeforeRegistration = true;
            }
            else if (!_requests.TryAdd(requestId, state))
            {
                throw new InvalidOperationException($"An analysis request named '{requestId}' is already active.");
            }
        }

        if (terminatedBeforeRegistration)
            yield break;

        using var registration = cancellationToken.Register(() => CancelRequest(requestId, state, cancellationToken));
        try
        {
            var request = CreateRequest(position, requestId);
            var json = JsonSerializer.Serialize(request, JsonOptions);
            await _transport.WriteLineAsync(json, cancellationToken).ConfigureAwait(false);

            await foreach (var result in state.Results.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return result;
        }
        finally
        {
            if (_requests.TryRemove(new KeyValuePair<string, RequestState>(requestId, state)))
                state.Results.Writer.TryComplete();
        }
    }

    public async Task TerminateAsync(string requestId, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        _terminateIntents.TryAdd(requestId, 0);
        var request = new KataGoTerminateRequest($"stop-{requestId}", "terminate", requestId);
        var json = JsonSerializer.Serialize(request, JsonOptions);
        await _transport.WriteLineAsync(json, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        FailAll(new ObjectDisposedException(nameof(KataGoAnalysisClient)));
        _lifetime.Cancel();
        try
        {
            await _readLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            await _transport.DisposeAsync().ConfigureAwait(false);
            _lifetime.Dispose();
        }
    }

    private async Task ReadLoopAsync()
    {
        Exception? failure = null;
        try
        {
            await foreach (var line in _transport.ReadLinesAsync(_lifetime.Token).ConfigureAwait(false))
            {
                var response = JsonSerializer.Deserialize<KataGoAnalysisResponse>(line, JsonOptions)
                    ?? throw new JsonException("KataGo returned an empty JSON response.");
                if (string.IsNullOrWhiteSpace(response.Id))
                {
                    if (!string.IsNullOrWhiteSpace(response.Error))
                    {
                        failure = new InvalidOperationException(response.Error);
                        break;
                    }

                    continue;
                }
                if (!_requests.TryGetValue(response.Id, out var state)) continue;

                if (!string.IsNullOrWhiteSpace(response.Error))
                {
                    if (_requests.TryRemove(new KeyValuePair<string, RequestState>(response.Id, state)))
                    {
                        _terminateIntents.TryRemove(response.Id, out _);
                        state.Results.Writer.TryComplete(new InvalidOperationException(response.Error));
                    }
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(response.Warning) &&
                    response.MoveInfos is null && response.RootInfo is null)
                    continue;

                var result = Map(response);
                if (!state.Results.Writer.TryWrite(result)) continue;
                if (!response.IsDuringSearch &&
                    _requests.TryRemove(new KeyValuePair<string, RequestState>(response.Id, state)))
                {
                    _terminateIntents.TryRemove(response.Id, out _);
                    state.Results.Writer.TryComplete();
                }
            }

            if (!_lifetime.IsCancellationRequested)
                failure ??= new IOException("KataGo output ended before all analysis requests completed.");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            failure = error;
        }
        finally
        {
            if (failure is not null) FailAll(failure);
        }
    }

    private void CancelRequest(string requestId, RequestState state, CancellationToken cancellationToken)
    {
        if (_requests.TryRemove(new KeyValuePair<string, RequestState>(requestId, state)))
            state.Results.Writer.TryComplete(new OperationCanceledException(cancellationToken));
    }

    private void FailAll(Exception error)
    {
        lock (_lifecycleGate)
        {
            _terminalFailure ??= error;
            foreach (var pair in _requests.ToArray())
            {
                if (_requests.TryRemove(pair)) pair.Value.Results.Writer.TryComplete(_terminalFailure);
            }
        }
    }

    private static KataGoAnalysisRequest CreateRequest(AnalysisPosition position, string requestId) =>
        new(
            requestId,
            position.Moves.Select(move => ToKataGoMove(move, position.BoardSize)).ToArray(),
            ToPlayer(position.NextPlayer),
            "chinese",
            position.Komi,
            position.BoardSize,
            position.BoardSize,
            100_000_000,
            12,
            1.0,
            true);

    private static IReadOnlyList<string> ToKataGoMove(PlayedMove playedMove, int boardSize) =>
        [ToPlayer(playedMove.Color), ToKataGoCoordinate(playedMove.Move, boardSize)];

    private static string ToPlayer(StoneColor color) => color switch
    {
        StoneColor.Black => "B",
        StoneColor.White => "W",
        _ => throw new ArgumentOutOfRangeException(nameof(color))
    };

    private static string ToKataGoCoordinate(Move move, int boardSize)
    {
        if (move.Kind == MoveKind.Pass) return "pass";
        var column = move.Point.Column;
        var letter = (char)('A' + column + (column >= 8 ? 1 : 0));
        return $"{letter}{boardSize - move.Point.Row}";
    }

    private static AnalysisResult Map(KataGoAnalysisResponse response)
    {
        var candidates = response.MoveInfos?.Select(info =>
                new CandidateMove(info.Move, info.Winrate, info.ScoreLead, info.Visits))
            .ToArray() ?? [];
        return new AnalysisResult(
            response.Id!,
            !response.IsDuringSearch,
            candidates,
            response.RootInfo?.Winrate ?? 0,
            response.RootInfo?.ScoreLead ?? 0,
            response.Ownership);
    }

    private sealed class RequestState
    {
        public Channel<AnalysisResult> Results { get; } = Channel.CreateUnbounded<AnalysisResult>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    }
}
