using System.Text.Json.Serialization;

namespace Yijing.Infrastructure.KataGo;

internal sealed record KataGoAnalysisRequest(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("moves")] IReadOnlyList<IReadOnlyList<string>> Moves,
    [property: JsonPropertyName("initialPlayer")] string InitialPlayer,
    [property: JsonPropertyName("rules")] string Rules,
    [property: JsonPropertyName("komi")] double Komi,
    [property: JsonPropertyName("boardXSize")] int BoardXSize,
    [property: JsonPropertyName("boardYSize")] int BoardYSize,
    [property: JsonPropertyName("maxVisits")] int MaxVisits,
    [property: JsonPropertyName("analysisPVLen")] int AnalysisPvLen,
    [property: JsonPropertyName("reportDuringSearchEvery")] double ReportDuringSearchEvery,
    [property: JsonPropertyName("includeOwnership")] bool IncludeOwnership);

internal sealed record KataGoTerminateRequest(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("terminateId")] string TerminateId);

internal sealed record KataGoAnalysisResponse(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("isDuringSearch")] bool IsDuringSearch,
    [property: JsonPropertyName("turnNumber")] int TurnNumber,
    [property: JsonPropertyName("moveInfos")] IReadOnlyList<KataGoMoveInfo>? MoveInfos,
    [property: JsonPropertyName("rootInfo")] KataGoRootInfo? RootInfo,
    [property: JsonPropertyName("ownership")] IReadOnlyList<double>? Ownership,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("warning")] string? Warning);

internal sealed record KataGoMoveInfo(
    [property: JsonPropertyName("move")] string Move,
    [property: JsonPropertyName("winrate")] double Winrate,
    [property: JsonPropertyName("scoreLead")] double ScoreLead,
    [property: JsonPropertyName("visits")] int Visits);

internal sealed record KataGoRootInfo(
    [property: JsonPropertyName("winrate")] double Winrate,
    [property: JsonPropertyName("scoreLead")] double ScoreLead);
