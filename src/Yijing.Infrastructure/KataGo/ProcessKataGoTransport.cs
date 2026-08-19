using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Yijing.Infrastructure.KataGo;

public sealed class KataGoProcessExitedException(int exitCode)
    : IOException($"KataGo process exited unexpectedly with code {exitCode}.")
{
    public int ExitCode { get; } = exitCode;
}

public sealed class ProcessKataGoTransport : IKataGoTransport
{
    private readonly Process _process;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private int _disposed;

    public ProcessKataGoTransport(string executablePath, string modelPath, string configPath)
        : this(CreateStartInfo(executablePath, modelPath, configPath))
    {
    }

    public ProcessKataGoTransport(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!_process.Start()) throw new InvalidOperationException("KataGo process did not start.");
        _ = DrainStandardErrorAsync(_process);
    }

    public async ValueTask WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetime.Token);
        var enteredGate = false;
        try
        {
            await _writeGate.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
            enteredGate = true;
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            await _process.StandardInput.WriteLineAsync(line.AsMemory(), linkedCancellation.Token)
                .ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(linkedCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            _lifetime.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(ProcessKataGoTransport));
        }
        finally
        {
            if (enteredGate) _writeGate.Release();
        }
    }

    public async IAsyncEnumerable<string> ReadLinesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        while (true)
        {
            var line = await _process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                if (Volatile.Read(ref _disposed) != 0) yield break;
                await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                throw new KataGoProcessExitedException(_process.ExitCode);
            }
            yield return line;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _lifetime.Cancel();
        await _writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _process.StandardInput.Close();
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _process.Dispose();
            _writeGate.Release();
        }
    }

    private static ProcessStartInfo CreateStartInfo(string executablePath, string modelPath, string configPath)
        => CreateStartInfo(executablePath, modelPath, configPath, localAppData: null);

    internal static ProcessStartInfo CreateStartInfo(
        string executablePath,
        string modelPath,
        string configPath,
        string? localAppData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        var info = new ProcessStartInfo(executablePath);
        info.ArgumentList.Add("analysis");
        info.ArgumentList.Add("-model");
        info.ArgumentList.Add(modelPath);
        info.ArgumentList.Add("-config");
        info.ArgumentList.Add(configPath);
        EngineRuntime.ApplyToEnvironment(info, localAppData);
        return info;
    }

    private static async Task DrainStandardErrorAsync(Process process)
    {
        try
        {
            while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is not null)
            {
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }
}
