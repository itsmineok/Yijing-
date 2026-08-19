using System.Text.Json;

var crashAfterOne = args.Contains("--crash-after-one", StringComparer.Ordinal);
var responseCount = 0;

while (await Console.In.ReadLineAsync() is { } line)
{
    if (string.IsNullOrWhiteSpace(line))
        continue;

    using var request = JsonDocument.Parse(line);
    var root = request.RootElement;
    var id = root.TryGetProperty("id", out var idNode) ? idNode.GetString() ?? "fake" : "fake";

    if (root.TryGetProperty("action", out var actionNode) && actionNode.GetString() == "terminate")
    {
        await EmitAsync(new
        {
            id,
            action = "terminate",
            terminateId = root.TryGetProperty("terminateId", out var terminateNode)
                ? terminateNode.GetString()
                : null
        });
        responseCount++;
    }
    else
    {
        await EmitAnalysisAsync(id, "D4", isDuringSearch: true, visits: 128);
        responseCount++;
        if (crashAfterOne)
        {
            Environment.ExitCode = 23;
            return;
        }

        await Task.Delay(50);
        await EmitAnalysisAsync(id, "Q16", isDuringSearch: false, visits: 4096);
        responseCount++;
    }

    if (crashAfterOne && responseCount >= 1)
    {
        Environment.ExitCode = 23;
        return;
    }
}

static async Task EmitAnalysisAsync(string id, string move, bool isDuringSearch, int visits) =>
    await EmitAsync(new
    {
        id,
        isDuringSearch,
        turnNumber = 0,
        moveInfos = new[] { new { move, winrate = 0.5, scoreLead = 0.0, visits } },
        rootInfo = new { winrate = 0.5, scoreLead = 0.0 }
    });

static async Task EmitAsync<T>(T response)
{
    await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response));
    await Console.Out.FlushAsync();
}
