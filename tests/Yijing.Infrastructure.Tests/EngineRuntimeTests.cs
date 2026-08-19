using System.Diagnostics;
using Yijing.Infrastructure.KataGo;

namespace Yijing.Infrastructure.Tests;

public sealed class EngineRuntimeTests
{
    [Fact]
    public void GetRuntimeDirectory_IsUnderYijingEngineRuntimeInLocalAppData()
    {
        var directory = EngineRuntime.GetRuntimeDirectory(@"C:\Temp\LocalAppData");

        Assert.Equal(
            Path.GetFullPath(Path.Combine(@"C:\Temp\LocalAppData", "Yijing", "engine-runtime")),
            Path.GetFullPath(directory));
    }

    [Fact]
    public void ApplyToEnvironment_PrependsExistingRuntimeDirectoryToPath()
    {
        using var directory = new TemporaryDirectory();
        var runtimeDirectory = EngineRuntime.GetRuntimeDirectory(directory.Path);
        Directory.CreateDirectory(runtimeDirectory);
        var originalPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        var startInfo = new ProcessStartInfo("stub.exe");

        EngineRuntime.ApplyToEnvironment(startInfo, directory.Path);

        var path = startInfo.Environment["PATH"];
        Assert.StartsWith(runtimeDirectory + ";", path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(originalPath, path, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyToEnvironment_WithoutRuntimeDirectoryLeavesPathUnchanged()
    {
        using var directory = new TemporaryDirectory();
        var startInfo = new ProcessStartInfo("stub.exe");

        EngineRuntime.ApplyToEnvironment(startInfo, directory.Path);

        if (startInfo.Environment.TryGetValue("PATH", out var path))
        {
            Assert.DoesNotContain(
                EngineRuntime.GetRuntimeDirectory(directory.Path),
                path,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
