using System.Diagnostics;
using Yijing.Infrastructure.KataGo;

namespace Yijing.Infrastructure.Tests;

public sealed class ProcessKataGoTransportTests
{
    [Fact]
    public async Task Unexpected_process_end_exposes_the_exit_code()
    {
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        var startInfo = new ProcessStartInfo(executable);
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add("exit 23");
        await using var transport = new ProcessKataGoTransport(startInfo);
        await using var lines = transport.ReadLinesAsync(CancellationToken.None).GetAsyncEnumerator();

        var error = await Assert.ThrowsAsync<KataGoProcessExitedException>(async () =>
            await lines.MoveNextAsync());

        Assert.Equal(23, error.ExitCode);
    }

    [Fact]
    public async Task DisposeAsync_WaitsForActiveWriteBeforeClosingProcessResources()
    {
        var startInfo = CreateStalledReader();
        var transport = new ProcessKataGoTransport(startInfo);
        var payload = new string('x', 4 * 1024 * 1024);
        await using var lines = transport.ReadLinesAsync(CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await lines.MoveNextAsync());
        Assert.Equal("READY", lines.Current);

        var writeTask = transport.WriteLineAsync(payload, CancellationToken.None).AsTask();
        await Task.Delay(50);
        Assert.False(writeTask.IsCompleted);

        var disposeTask = transport.DisposeAsync().AsTask();
        var writeError = await Record.ExceptionAsync(() =>
            writeTask.WaitAsync(TimeSpan.FromSeconds(5)));
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));

        var disposed = Assert.IsType<ObjectDisposedException>(writeError);
        Assert.Contains(nameof(ProcessKataGoTransport), disposed.ObjectName, StringComparison.Ordinal);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            transport.WriteLineAsync("late", CancellationToken.None).AsTask());
    }

    [Fact]
    public void CreateStartInfo_PrependsEngineRuntimeDirectoryToChildPath()
    {
        using var directory = new TemporaryDirectory();
        var runtimeDirectory = EngineRuntime.GetRuntimeDirectory(directory.Path);
        Directory.CreateDirectory(runtimeDirectory);

        var startInfo = ProcessKataGoTransport.CreateStartInfo(
            "katago.exe", "model.bin.gz", "analysis.cfg", directory.Path);

        var path = startInfo.Environment["PATH"];
        Assert.StartsWith(runtimeDirectory + ";", path, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("analysis", startInfo.ArgumentList[0]);
        Assert.Equal("-model", startInfo.ArgumentList[1]);
        Assert.Equal("model.bin.gz", startInfo.ArgumentList[2]);
        Assert.Equal("-config", startInfo.ArgumentList[3]);
        Assert.Equal("analysis.cfg", startInfo.ArgumentList[4]);
    }

    private static ProcessStartInfo CreateStalledReader()
    {
        var executable = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        var startInfo = new ProcessStartInfo(executable);
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(
            "[Console]::Out.WriteLine('READY'); [Console]::Out.Flush(); Start-Sleep -Seconds 3");
        return startInfo;
    }
}
