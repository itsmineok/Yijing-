using Yijing.Infrastructure.Diagnostics;

namespace Yijing.Infrastructure.Tests;

public sealed class RollingFileLoggerTests
{
    [Fact]
    public async Task Logger_rotates_at_bound_keeps_seven_and_records_only_structured_engine_fields()
    {
        using var directory = new TemporaryDirectory();
        var logger = new RollingFileLogger(directory.Path, maxBytes: 180, retainedFiles: 7);

        for (var index = 0; index < 30; index++)
            await logger.WriteEngineAsync(new EngineLogEntry(
                "v1.17.1", "Eigen", index, $"request-{index}", 42, "IOException"));

        var files = Directory.GetFiles(directory.Path, "yijing*.log");
        Assert.InRange(files.Length, 1, 8); // active plus at most seven archives
        var text = string.Join("\n", files.Select(File.ReadAllText));
        Assert.Contains("\"backend\":\"Eigen\"", text);
        Assert.DoesNotContain("moves", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("player", text, StringComparison.OrdinalIgnoreCase);
    }
}
