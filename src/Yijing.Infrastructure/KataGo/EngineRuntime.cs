using System.Diagnostics;

namespace Yijing.Infrastructure.KataGo;

public static class EngineRuntime
{
    public static string GetRuntimeDirectory(string? localAppData = null)
    {
        var root = localAppData
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.GetFullPath(Path.Combine(root, "Yijing", "engine-runtime"));
    }

    public static void ApplyToEnvironment(ProcessStartInfo startInfo, string? localAppData = null)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        var directory = GetRuntimeDirectory(localAppData);
        if (!Directory.Exists(directory)) return;

        var basePath = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (startInfo.Environment.TryGetValue("PATH", out var existing) && existing is not null)
            basePath = existing;
        startInfo.Environment["PATH"] = basePath.Length == 0
            ? directory
            : directory + ";" + basePath;
    }
}
