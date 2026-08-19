# 弈境 Windows 围棋软件 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 构建一款完全离线运行的 Windows 10/11 x64 围棋桌面软件，支持中国规则、人机/双人对局、SGF、专业分析界面以及 KataGo 极限棋力自动后端。

**Architecture:** 使用 WPF + MVVM 构建桌面表现层，将围棋规则、对局状态、KataGo 进程、SGF 和本地存储隔离为独立项目。所有棋局变更生成新 `BoardState`；KataGo 通过逐行 JSON 异步协议工作，分析结果以 `GameRevision` 防止过期数据污染 UI。

**Tech Stack:** .NET 10 LTS、C# 14、WPF、xUnit、System.Text.Json、KataGo v1.17.x JSON Analysis Engine、PowerShell、Inno Setup 6。

## Global Constraints

- 目标平台固定为 Windows 10/11 x64；发布为 .NET 10 LTS 自包含程序。
- 软件完全离线运行；安装后不要求账号、云服务或额外下载。
- 支持 19×19、13×13、9×9；固定中国规则，白贴 7.5 目。
- 人机模式允许玩家执黑、执白或随机执棋；AI 不提供难度选择。
- AI 正式落子每手最多搜索 30 秒，GPU 失败自动回退 Eigen CPU。
- 人机悔棋回到玩家上次落子前；本地双人每次仅撤销最后一手。
- 每次落子后原子保存恢复快照；SGF 使用 UTF-8 与 FF[4]。
- 首版不包含联网对战、账号、云同步、模型训练和变化树编辑。
- WPF UI 线程不得同步等待 KataGo、磁盘 IO 或 SHA-256 校验。
- 所有产品界面文案使用简体中文，内部类型和成员使用英文。

---

## File Structure

```text
Yijing.sln
Directory.Build.props
src/
  Yijing.Domain/
    Board/StoneColor.cs              棋子颜色与对手色
    Board/BoardPoint.cs               零基行列坐标
    Board/Move.cs                     落子或停一手
    Board/BoardState.cs               不可变棋盘快照与历史局面键
    Rules/GoRules.cs                  合法性、提子、自杀、超级劫
    Rules/MoveResult.cs               规则执行结果
    Scoring/ChineseAreaScorer.cs      中国面积计分
    Scoring/ScoreResult.cs            黑白面积、贴目、胜者和目差
  Yijing.Application/
    Games/GameMode.cs                 人机或本地双人
    Games/GameOptions.cs              棋盘、执棋和规则参数
    Games/PlayedMove.cs               带颜色的历史着法
    Games/GameResult.cs               数子、认输和 SGF 结果
    Games/GameSession.cs              对局状态机与悔棋策略
    Analysis/AnalysisContracts.cs     分析请求、候选点、结果接口
    Analysis/AnalysisCoordinator.cs   GameRevision、取消和 30 秒搜索
    Persistence/IGameStore.cs         恢复快照接口
  Yijing.Infrastructure/
    KataGo/KataGoDtos.cs              JSON 协议 DTO
    KataGo/IKataGoTransport.cs         可测试逐行传输接口
    KataGo/ProcessKataGoTransport.cs   子进程 stdin/stdout/stderr
    KataGo/KataGoAnalysisClient.cs     请求关联与 terminate 动作
    KataGo/EngineCandidate.cs          后端、模型与启动信息
    KataGo/BackendSelector.cs          自检、基准和回退
    KataGo/EngineManifest.cs           固定资源清单
    Sgf/SgfGame.cs                     SGF 主线模型
    Sgf/SgfReader.cs                   FF[4] 主线读取
    Sgf/SgfWriter.cs                   UTF-8 SGF 输出
    Storage/AtomicJsonStore.cs         临时文件 + 原子替换
    Storage/LocalGameStore.cs          autosave/settings/profile 路径
  Yijing.Desktop/
    App.xaml                           主题资源与启动入口
    App.xaml.cs                        组合根、恢复提示和引擎启动
    MainWindow.xaml                    专业分析台布局
    MainWindow.xaml.cs                 仅窗口级生命周期
    Controls/GoBoardControl.cs         棋盘绘制和点击命中
    Controls/BoardRenderPalette.cs     棋盘色彩和尺寸常量
    ViewModels/ObservableObject.cs     属性通知基类
    ViewModels/RelayCommand.cs         同步/异步命令
    ViewModels/MainWindowViewModel.cs  导航和全局命令
    ViewModels/GameViewModel.cs        棋盘、操作和分析绑定
    ViewModels/NewGameViewModel.cs     新对局选项
    Views/NewGameDialog.xaml           模式、黑白、棋盘选择
    Views/ScoringDialog.xaml           死子切换和终局确认
    Services/DialogService.cs          WPF 对话框适配
    Converters/StoneConverters.cs      显示转换
tests/
  Yijing.Domain.Tests/
    BoardPointTests.cs
    GoRulesTests.cs
    ChineseAreaScorerTests.cs
  Yijing.Application.Tests/
    GameSessionTests.cs
    AnalysisCoordinatorTests.cs
  Yijing.Infrastructure.Tests/
    SgfReaderWriterTests.cs
    AtomicJsonStoreTests.cs
    KataGoAnalysisClientTests.cs
    BackendSelectorTests.cs
  Yijing.Desktop.Tests/
    GameViewModelTests.cs
tools/
  FakeKataGo/FakeKataGo.csproj
  FakeKataGo/Program.cs
assets/katago/
  engine-manifest.json
  analysis.cfg
scripts/
  Fetch-KataGoAssets.ps1
  Verify-KataGoAssets.ps1
  Run-Acceptance.ps1
packaging/
  Yijing.iss
  THIRD-PARTY-NOTICES.txt
```

---

### Task 1: 安装工具链并建立可测试解决方案

**Files:**
- Create: `Yijing.sln`
- Create: `Directory.Build.props`
- Create: `src/Yijing.Domain/Yijing.Domain.csproj`
- Create: `src/Yijing.Application/Yijing.Application.csproj`
- Create: `src/Yijing.Infrastructure/Yijing.Infrastructure.csproj`
- Create: `src/Yijing.Desktop/Yijing.Desktop.csproj`
- Create: `tests/Yijing.Domain.Tests/Yijing.Domain.Tests.csproj`
- Create: `tests/Yijing.Application.Tests/Yijing.Application.Tests.csproj`
- Create: `tests/Yijing.Infrastructure.Tests/Yijing.Infrastructure.Tests.csproj`
- Create: `tests/Yijing.Desktop.Tests/Yijing.Desktop.Tests.csproj`
- Create: `src/Yijing.Domain/Board/StoneColor.cs`
- Create: `src/Yijing.Domain/Board/BoardPoint.cs`
- Create: `src/Yijing.Domain/Board/Move.cs`
- Test: `tests/Yijing.Domain.Tests/BoardPointTests.cs`

**Interfaces:**
- Consumes: none.
- Produces: `StoneColor`, `BoardPoint`, `MoveKind`, `Move`; four projects and four test projects referenced by `Yijing.sln`.

- [ ] **Step 1: Install and verify the .NET 10 SDK**

Run:

```powershell
winget install --id Microsoft.DotNet.SDK.10 --exact --accept-package-agreements --accept-source-agreements
& "$env:ProgramFiles\dotnet\dotnet.exe" --version
```

Expected: installation succeeds and `dotnet --version` prints `10.0.x`.

- [ ] **Step 2: Scaffold the solution and project references**

Run:

```powershell
dotnet new sln -n Yijing --format sln
dotnet new classlib -n Yijing.Domain -o src/Yijing.Domain -f net10.0
dotnet new classlib -n Yijing.Application -o src/Yijing.Application -f net10.0
dotnet new classlib -n Yijing.Infrastructure -o src/Yijing.Infrastructure -f net10.0
dotnet new wpf -n Yijing.Desktop -o src/Yijing.Desktop -f net10.0
dotnet new xunit -n Yijing.Domain.Tests -o tests/Yijing.Domain.Tests -f net10.0
dotnet new xunit -n Yijing.Application.Tests -o tests/Yijing.Application.Tests -f net10.0
dotnet new xunit -n Yijing.Infrastructure.Tests -o tests/Yijing.Infrastructure.Tests -f net10.0
dotnet new xunit -n Yijing.Desktop.Tests -o tests/Yijing.Desktop.Tests -f net10.0-windows
dotnet sln Yijing.sln add src/Yijing.Domain src/Yijing.Application src/Yijing.Infrastructure src/Yijing.Desktop tests/Yijing.Domain.Tests tests/Yijing.Application.Tests tests/Yijing.Infrastructure.Tests tests/Yijing.Desktop.Tests
dotnet add src/Yijing.Application reference src/Yijing.Domain
dotnet add src/Yijing.Infrastructure reference src/Yijing.Domain src/Yijing.Application
dotnet add src/Yijing.Desktop reference src/Yijing.Domain src/Yijing.Application src/Yijing.Infrastructure
dotnet add tests/Yijing.Domain.Tests reference src/Yijing.Domain
dotnet add tests/Yijing.Application.Tests reference src/Yijing.Domain src/Yijing.Application
dotnet add tests/Yijing.Infrastructure.Tests reference src/Yijing.Domain src/Yijing.Application src/Yijing.Infrastructure
dotnet add tests/Yijing.Desktop.Tests reference src/Yijing.Domain src/Yijing.Application src/Yijing.Infrastructure src/Yijing.Desktop
```

Expected: all commands exit `0`; `dotnet sln Yijing.sln list` prints eight projects.

- [ ] **Step 3: Add repository-wide compiler settings**

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <LangVersion>14.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
</Project>
```

Delete every generated `Class1.cs` and test `UnitTest1.cs`.

- [ ] **Step 4: Write the failing primitive test**

Create `tests/Yijing.Domain.Tests/BoardPointTests.cs`:

```csharp
using Yijing.Domain.Board;

namespace Yijing.Domain.Tests;

public sealed class BoardPointTests
{
    [Fact]
    public void IsInside_UsesZeroBasedSquareBounds()
    {
        Assert.True(new BoardPoint(0, 0).IsInside(19));
        Assert.True(new BoardPoint(18, 18).IsInside(19));
        Assert.False(new BoardPoint(-1, 0).IsInside(19));
        Assert.False(new BoardPoint(19, 0).IsInside(19));
    }

    [Fact]
    public void Opponent_SwitchesPlayableColors()
    {
        Assert.Equal(StoneColor.White, StoneColor.Black.Opponent());
        Assert.Equal(StoneColor.Black, StoneColor.White.Opponent());
    }
}
```

- [ ] **Step 5: Run the test and verify the missing types fail compilation**

Run: `dotnet test tests/Yijing.Domain.Tests/Yijing.Domain.Tests.csproj --filter BoardPointTests`

Expected: FAIL with `CS0246` for `BoardPoint` or `StoneColor`.

- [ ] **Step 6: Implement the domain primitives**

Create `src/Yijing.Domain/Board/StoneColor.cs`:

```csharp
namespace Yijing.Domain.Board;

public enum StoneColor { Empty = 0, Black = 1, White = 2 }

public static class StoneColorExtensions
{
    public static StoneColor Opponent(this StoneColor color) => color switch
    {
        StoneColor.Black => StoneColor.White,
        StoneColor.White => StoneColor.Black,
        _ => throw new ArgumentOutOfRangeException(nameof(color), color, "空点没有对手色。")
    };
}
```

Create `src/Yijing.Domain/Board/BoardPoint.cs`:

```csharp
namespace Yijing.Domain.Board;

public readonly record struct BoardPoint(int Row, int Column)
{
    public bool IsInside(int boardSize) =>
        boardSize is >= 2 and <= 19 && Row >= 0 && Row < boardSize && Column >= 0 && Column < boardSize;
}
```

Create `src/Yijing.Domain/Board/Move.cs`:

```csharp
namespace Yijing.Domain.Board;

public enum MoveKind { Play, Pass }

public readonly record struct Move(MoveKind Kind, BoardPoint Point)
{
    public static Move Play(BoardPoint point) => new(MoveKind.Play, point);
    public static Move Pass() => new(MoveKind.Pass, default);
}
```

- [ ] **Step 7: Run all tests and commit**

Run: `dotnet test Yijing.sln`

Expected: PASS with 2 tests and 0 failures.

```powershell
git add Yijing.sln Directory.Build.props src tests
git commit -m "build: scaffold Yijing .NET solution"
```

---

### Task 2: 实现不可变棋盘和中国规则落子

**Files:**
- Create: `src/Yijing.Domain/Board/BoardState.cs`
- Create: `src/Yijing.Domain/Rules/MoveResult.cs`
- Create: `src/Yijing.Domain/Rules/GoRules.cs`
- Test: `tests/Yijing.Domain.Tests/GoRulesTests.cs`

**Interfaces:**
- Consumes: `StoneColor`, `BoardPoint`, `Move` from Task 1.
- Produces: `BoardState.Empty(int)`, `BoardState.FromSetup(...)`, `GoRules.TryApply(BoardState, Move)`, `MoveResult`.

- [ ] **Step 1: Write capture, suicide, pass, and superko tests**

Create `tests/Yijing.Domain.Tests/GoRulesTests.cs`:

```csharp
using Yijing.Domain.Board;
using Yijing.Domain.Rules;

namespace Yijing.Domain.Tests;

public sealed class GoRulesTests
{
    [Fact]
    public void Play_CapturesOpponentGroupWithoutLiberties()
    {
        var state = BoardState.FromSetup(5,
            [
                (new BoardPoint(1, 1), StoneColor.White),
                (new BoardPoint(0, 1), StoneColor.Black),
                (new BoardPoint(1, 0), StoneColor.Black),
                (new BoardPoint(2, 1), StoneColor.Black)
            ], StoneColor.Black);

        var result = GoRules.TryApply(state, Move.Play(new BoardPoint(1, 2)));

        Assert.True(result.IsLegal);
        Assert.Equal(StoneColor.Empty, result.State!.At(new BoardPoint(1, 1)));
        Assert.Equal(1, result.CapturedStones);
        Assert.Equal(StoneColor.White, result.State.NextPlayer);
    }

    [Fact]
    public void Play_RejectsSuicideWithoutChangingState()
    {
        var state = BoardState.FromSetup(3,
            [
                (new BoardPoint(0, 1), StoneColor.White),
                (new BoardPoint(1, 0), StoneColor.White),
                (new BoardPoint(1, 2), StoneColor.White),
                (new BoardPoint(2, 1), StoneColor.White)
            ], StoneColor.Black);

        var result = GoRules.TryApply(state, Move.Play(new BoardPoint(1, 1)));

        Assert.False(result.IsLegal);
        Assert.Equal(IllegalMoveReason.Suicide, result.IllegalReason);
        Assert.Same(state, result.OriginalState);
    }

    [Fact]
    public void Pass_TwiceMarksEndByConsecutivePasses()
    {
        var first = GoRules.TryApply(BoardState.Empty(9), Move.Pass()).State!;
        var second = GoRules.TryApply(first, Move.Pass()).State!;

        Assert.Equal(2, second.ConsecutivePasses);
        Assert.True(second.HasTwoConsecutivePasses);
    }

    [Fact]
    public void Play_RejectsImmediateKoRecaptureByPositionalSuperko()
    {
        var state = BoardState.FromSetup(5,
            [
                (new BoardPoint(0, 1), StoneColor.Black),
                (new BoardPoint(1, 0), StoneColor.Black),
                (new BoardPoint(2, 1), StoneColor.Black),
                (new BoardPoint(1, 1), StoneColor.White),
                (new BoardPoint(0, 2), StoneColor.White),
                (new BoardPoint(2, 2), StoneColor.White),
                (new BoardPoint(1, 3), StoneColor.White)
            ], StoneColor.Black);

        var capture = GoRules.TryApply(state, Move.Play(new BoardPoint(1, 2))).State!;
        var recapture = GoRules.TryApply(capture, Move.Play(new BoardPoint(1, 1)));

        Assert.False(recapture.IsLegal);
        Assert.Equal(IllegalMoveReason.PositionalSuperko, recapture.IllegalReason);
    }
}
```

- [ ] **Step 2: Run the tests and verify they fail**

Run: `dotnet test tests/Yijing.Domain.Tests/Yijing.Domain.Tests.csproj --filter GoRulesTests`

Expected: FAIL with `CS0246` for `BoardState` and `GoRules`.

- [ ] **Step 3: Implement immutable board snapshots**

Create `src/Yijing.Domain/Board/BoardState.cs` with this public surface and storage behavior:

```csharp
namespace Yijing.Domain.Board;

public sealed class BoardState
{
    private readonly StoneColor[] _cells;
    private readonly HashSet<string> _seenPositionKeys;

    private BoardState(int size, StoneColor[] cells, StoneColor nextPlayer,
        int consecutivePasses, int moveNumber, HashSet<string> seenPositionKeys)
    {
        Size = size;
        _cells = cells;
        NextPlayer = nextPlayer;
        ConsecutivePasses = consecutivePasses;
        MoveNumber = moveNumber;
        _seenPositionKeys = seenPositionKeys;
    }

    public int Size { get; }
    public StoneColor NextPlayer { get; }
    public int ConsecutivePasses { get; }
    public int MoveNumber { get; }
    public bool HasTwoConsecutivePasses => ConsecutivePasses >= 2;

    public StoneColor At(BoardPoint point)
    {
        if (!point.IsInside(Size)) throw new ArgumentOutOfRangeException(nameof(point));
        return _cells[(point.Row * Size) + point.Column];
    }

    public static BoardState Empty(int size) => FromSetup(size, [], StoneColor.Black);

    public static BoardState FromSetup(int size,
        IEnumerable<(BoardPoint Point, StoneColor Color)> stones,
        StoneColor nextPlayer)
    {
        if (size is < 2 or > 19)
            throw new ArgumentOutOfRangeException(nameof(size));
        if (nextPlayer == StoneColor.Empty)
            throw new ArgumentOutOfRangeException(nameof(nextPlayer));

        var cells = new StoneColor[size * size];
        foreach (var (point, color) in stones)
        {
            if (!point.IsInside(size) || color == StoneColor.Empty)
                throw new ArgumentException("初始棋子无效。", nameof(stones));
            var index = (point.Row * size) + point.Column;
            if (cells[index] != StoneColor.Empty)
                throw new ArgumentException("初始棋子坐标重复。", nameof(stones));
            cells[index] = color;
        }

        var key = PositionKey(cells);
        return new BoardState(size, cells, nextPlayer, 0, 0, new HashSet<string> { key });
    }

    internal StoneColor[] CloneCells() => (StoneColor[])_cells.Clone();
    internal bool HasSeen(string key) => _seenPositionKeys.Contains(key);
    internal HashSet<string> CloneSeen() => new(_seenPositionKeys, StringComparer.Ordinal);
    internal static string PositionKey(StoneColor[] cells) =>
        string.Create(cells.Length, cells, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
                span[i] = source[i] switch { StoneColor.Black => 'B', StoneColor.White => 'W', _ => '.' };
        });

    internal static BoardState AfterMove(BoardState source, StoneColor[] cells,
        StoneColor nextPlayer, int consecutivePasses, HashSet<string> seen) =>
        new(source.Size, cells, nextPlayer, consecutivePasses, source.MoveNumber + 1, seen);

    public BoardState ResumeAfterScoringDispute() =>
        new(Size, CloneCells(), NextPlayer, 0, MoveNumber, CloneSeen());
}
```

- [ ] **Step 4: Implement rule results and the move algorithm**

Create `src/Yijing.Domain/Rules/MoveResult.cs`:

```csharp
using Yijing.Domain.Board;

namespace Yijing.Domain.Rules;

public enum IllegalMoveReason { OutsideBoard, Occupied, Suicide, PositionalSuperko }

public sealed record MoveResult(
    BoardState OriginalState,
    BoardState? State,
    IllegalMoveReason? IllegalReason,
    int CapturedStones)
{
    public bool IsLegal => State is not null;
    public static MoveResult Legal(BoardState original, BoardState next, int captured = 0) =>
        new(original, next, null, captured);
    public static MoveResult Illegal(BoardState original, IllegalMoveReason reason) =>
        new(original, null, reason, 0);
}
```

Create `src/Yijing.Domain/Rules/GoRules.cs`:

```csharp
using Yijing.Domain.Board;

namespace Yijing.Domain.Rules;

public static class GoRules
{
    public static MoveResult TryApply(BoardState state, Move move)
    {
        if (move.Kind == MoveKind.Pass)
        {
            var passed = BoardState.AfterMove(state, state.CloneCells(),
                state.NextPlayer.Opponent(), state.ConsecutivePasses + 1, state.CloneSeen());
            return MoveResult.Legal(state, passed);
        }

        var point = move.Point;
        if (!point.IsInside(state.Size)) return MoveResult.Illegal(state, IllegalMoveReason.OutsideBoard);
        if (state.At(point) != StoneColor.Empty) return MoveResult.Illegal(state, IllegalMoveReason.Occupied);

        var cells = state.CloneCells();
        var color = state.NextPlayer;
        cells[Index(point, state.Size)] = color;
        var captured = 0;

        foreach (var neighbor in Neighbors(point, state.Size))
        {
            if (cells[Index(neighbor, state.Size)] != color.Opponent()) continue;
            var group = CollectGroup(cells, state.Size, neighbor);
            if (CountLiberties(cells, state.Size, group) != 0) continue;
            foreach (var stone in group) cells[Index(stone, state.Size)] = StoneColor.Empty;
            captured += group.Count;
        }

        var ownGroup = CollectGroup(cells, state.Size, point);
        if (CountLiberties(cells, state.Size, ownGroup) == 0)
            return MoveResult.Illegal(state, IllegalMoveReason.Suicide);

        var key = BoardState.PositionKey(cells);
        if (state.HasSeen(key)) return MoveResult.Illegal(state, IllegalMoveReason.PositionalSuperko);

        var seen = state.CloneSeen();
        seen.Add(key);
        var next = BoardState.AfterMove(state, cells, color.Opponent(), 0, seen);
        return MoveResult.Legal(state, next, captured);
    }

    private static int Index(BoardPoint point, int size) => (point.Row * size) + point.Column;

    private static IEnumerable<BoardPoint> Neighbors(BoardPoint point, int size)
    {
        var candidates = new[]
        {
            new BoardPoint(point.Row - 1, point.Column),
            new BoardPoint(point.Row + 1, point.Column),
            new BoardPoint(point.Row, point.Column - 1),
            new BoardPoint(point.Row, point.Column + 1)
        };
        return candidates.Where(candidate => candidate.IsInside(size));
    }

    private static List<BoardPoint> CollectGroup(StoneColor[] cells, int size, BoardPoint start)
    {
        var color = cells[Index(start, size)];
        var found = new HashSet<BoardPoint> { start };
        var queue = new Queue<BoardPoint>();
        queue.Enqueue(start);
        while (queue.TryDequeue(out var point))
        {
            foreach (var neighbor in Neighbors(point, size))
            {
                if (cells[Index(neighbor, size)] == color && found.Add(neighbor)) queue.Enqueue(neighbor);
            }
        }
        return [.. found];
    }

    private static int CountLiberties(StoneColor[] cells, int size, IReadOnlyList<BoardPoint> group)
    {
        var liberties = new HashSet<BoardPoint>();
        foreach (var point in group)
            foreach (var neighbor in Neighbors(point, size))
                if (cells[Index(neighbor, size)] == StoneColor.Empty) liberties.Add(neighbor);
        return liberties.Count;
    }
}
```

- [ ] **Step 5: Run rule tests and commit**

Run: `dotnet test tests/Yijing.Domain.Tests/Yijing.Domain.Tests.csproj --filter GoRulesTests`

Expected: PASS with 4 tests and 0 failures.

```powershell
git add src/Yijing.Domain tests/Yijing.Domain.Tests
git commit -m "feat: implement Chinese-rule move legality"
```

---

### Task 3: 实现中国面积计分

**Files:**
- Create: `src/Yijing.Domain/Scoring/ScoreResult.cs`
- Create: `src/Yijing.Domain/Scoring/ChineseAreaScorer.cs`
- Test: `tests/Yijing.Domain.Tests/ChineseAreaScorerTests.cs`

**Interfaces:**
- Consumes: `BoardState`, `BoardPoint`, `StoneColor`.
- Produces: `ChineseAreaScorer.Score(BoardState, IReadOnlySet<BoardPoint>, double) -> ScoreResult`.

- [ ] **Step 1: Write territory and komi tests**

Create `tests/Yijing.Domain.Tests/ChineseAreaScorerTests.cs`:

```csharp
using Yijing.Domain.Board;
using Yijing.Domain.Scoring;

namespace Yijing.Domain.Tests;

public sealed class ChineseAreaScorerTests
{
    [Fact]
    public void Score_CountsLivingStonesAndSurroundedEmptyPoints()
    {
        var state = BoardState.FromSetup(3,
            [
                (new BoardPoint(0, 1), StoneColor.Black),
                (new BoardPoint(1, 0), StoneColor.Black),
                (new BoardPoint(1, 2), StoneColor.Black),
                (new BoardPoint(2, 1), StoneColor.Black)
            ], StoneColor.Black);

        var result = ChineseAreaScorer.Score(state, new HashSet<BoardPoint>(), 7.5);

        Assert.Equal(9, result.BlackArea);
        Assert.Equal(7.5, result.WhiteTotal);
        Assert.Equal(StoneColor.Black, result.Winner);
        Assert.Equal(1.5, result.Margin);
    }

    [Fact]
    public void Score_RemovesMarkedDeadStonesBeforeAreaCounting()
    {
        var dead = new BoardPoint(1, 1);
        var state = BoardState.FromSetup(3,
            [
                (dead, StoneColor.White),
                (new BoardPoint(0, 1), StoneColor.Black),
                (new BoardPoint(1, 0), StoneColor.Black),
                (new BoardPoint(1, 2), StoneColor.Black),
                (new BoardPoint(2, 1), StoneColor.Black)
            ], StoneColor.Black);

        var result = ChineseAreaScorer.Score(state, new HashSet<BoardPoint> { dead }, 7.5);

        Assert.Equal(9, result.BlackArea);
        Assert.Equal(0, result.WhiteArea);
    }
}
```

- [ ] **Step 2: Run the tests and verify they fail**

Run: `dotnet test tests/Yijing.Domain.Tests/Yijing.Domain.Tests.csproj --filter ChineseAreaScorerTests`

Expected: FAIL with `CS0246` for `ChineseAreaScorer`.

- [ ] **Step 3: Implement flood-fill area scoring**

Create `src/Yijing.Domain/Scoring/ScoreResult.cs`:

```csharp
using Yijing.Domain.Board;

namespace Yijing.Domain.Scoring;

public sealed record ScoreResult(int BlackArea, int WhiteArea, double Komi,
    StoneColor Winner, double Margin)
{
    public double WhiteTotal => WhiteArea + Komi;
}
```

Create `src/Yijing.Domain/Scoring/ChineseAreaScorer.cs` so it copies the board, clears every point in `deadStones`, counts each remaining stone, flood-fills every unvisited empty region, and awards that region only when all bordering non-empty colors form the singleton set `{ Black }` or `{ White }`. Finish with:

```csharp
var whiteTotal = whiteArea + komi;
var winner = blackArea > whiteTotal ? StoneColor.Black : StoneColor.White;
var margin = Math.Abs(blackArea - whiteTotal);
return new ScoreResult(blackArea, whiteArea, komi, winner, margin);
```

Use orthogonal neighbors only. A region touching both colors is neutral and adds to neither side.

- [ ] **Step 4: Run scoring and domain tests, then commit**

Run: `dotnet test tests/Yijing.Domain.Tests/Yijing.Domain.Tests.csproj`

Expected: PASS with 8 tests and 0 failures.

```powershell
git add src/Yijing.Domain/Scoring tests/Yijing.Domain.Tests/ChineseAreaScorerTests.cs
git commit -m "feat: add Chinese area scoring"
```

---

### Task 4: 实现对局状态、认输与正确悔棋

**Files:**
- Create: `src/Yijing.Application/Games/GameMode.cs`
- Create: `src/Yijing.Application/Games/GameOptions.cs`
- Create: `src/Yijing.Application/Games/PlayedMove.cs`
- Create: `src/Yijing.Application/Games/GameResult.cs`
- Create: `src/Yijing.Application/Games/GameSession.cs`
- Test: `tests/Yijing.Application.Tests/GameSessionTests.cs`

**Interfaces:**
- Consumes: `BoardState`, `GoRules`, `Move`, `StoneColor`.
- Produces: `GameSession.Play(Move)`, `GameSession.Undo()`, `GameSession.Resign(StoneColor)`, `GameSession.RestoreAfterFinish()`.

- [ ] **Step 1: Write tests for human-vs-AI undo, local undo, pass, and resignation**

Create `tests/Yijing.Application.Tests/GameSessionTests.cs`:

```csharp
using Yijing.Application.Games;
using Yijing.Domain.Board;

namespace Yijing.Application.Tests;

public sealed class GameSessionTests
{
    [Fact]
    public void Undo_AfterAiReplyReturnsToBeforeHumansLastMove()
    {
        var game = GameSession.Start(new GameOptions(GameMode.HumanVsAi, 9, StoneColor.Black, 7.5));
        game.Play(Move.Play(new BoardPoint(2, 2)));
        game.Play(Move.Play(new BoardPoint(6, 6)));

        Assert.True(game.Undo());
        Assert.Equal(0, game.Moves.Count);
        Assert.Equal(StoneColor.Black, game.State.NextPlayer);
    }

    [Fact]
    public void Undo_WhileAiIsThinkingRemovesOnlyPendingHumanMove()
    {
        var game = GameSession.Start(new GameOptions(GameMode.HumanVsAi, 9, StoneColor.Black, 7.5));
        game.Play(Move.Play(new BoardPoint(2, 2)));
        game.SetAiThinking(true);

        Assert.True(game.Undo());
        Assert.Empty(game.Moves);
        Assert.Equal(StoneColor.Black, game.State.NextPlayer);
    }

    [Fact]
    public void Undo_DoesNotRemoveOpeningAiMoveBeforeWhiteHasPlayed()
    {
        var game = GameSession.Start(new GameOptions(GameMode.HumanVsAi, 9, StoneColor.White, 7.5));
        game.Play(Move.Play(new BoardPoint(2, 2)));

        Assert.False(game.Undo());
        Assert.Single(game.Moves);
    }

    [Fact]
    public void Undo_LocalTwoPlayerRemovesExactlyOneMove()
    {
        var game = GameSession.Start(new GameOptions(GameMode.LocalTwoPlayer, 9, null, 7.5));
        game.Play(Move.Play(new BoardPoint(2, 2)));
        game.Play(Move.Play(new BoardPoint(6, 6)));

        Assert.True(game.Undo());
        Assert.Single(game.Moves);
        Assert.Equal(StoneColor.White, game.State.NextPlayer);
    }

    [Fact]
    public void Resign_StoresSgfResultForOpponent()
    {
        var game = GameSession.Start(new GameOptions(GameMode.HumanVsAi, 19, StoneColor.Black, 7.5));

        game.Resign(StoneColor.Black);

        Assert.Equal("W+R", game.Result!.SgfValue);
        Assert.Equal(GameEndReason.Resignation, game.Result.Reason);
    }
}
```

- [ ] **Step 2: Run tests and verify missing application types fail**

Run: `dotnet test tests/Yijing.Application.Tests/Yijing.Application.Tests.csproj --filter GameSessionTests`

Expected: FAIL with `CS0246` for `GameSession`.

- [ ] **Step 3: Implement options and result types**

Create these exact records and enums:

```csharp
namespace Yijing.Application.Games;

public enum GameMode { HumanVsAi, LocalTwoPlayer }
public enum GameEndReason { Score, Resignation }

public sealed record GameOptions(GameMode Mode, int BoardSize,
    Yijing.Domain.Board.StoneColor? HumanColor, double Komi);

public sealed record PlayedMove(Yijing.Domain.Board.StoneColor Color,
    Yijing.Domain.Board.Move Move);

public sealed record GameResult(Yijing.Domain.Board.StoneColor Winner,
    GameEndReason Reason, double? Margin)
{
    public string SgfValue => Reason == GameEndReason.Resignation
        ? $"{(Winner == Yijing.Domain.Board.StoneColor.Black ? "B" : "W")}+R"
        : $"{(Winner == Yijing.Domain.Board.StoneColor.Black ? "B" : "W")}+{Margin:0.0}";
}
```

- [ ] **Step 4: Implement `GameSession` history and undo policy**

`GameSession` owns `List<BoardState> _states` with the initial state at index 0 and `List<PlayedMove> _moves`. `Play` calls `GoRules.TryApply`, appends only legal outcomes, and clears `IsAiThinking`. `Undo` uses this exact policy:

```csharp
public bool Undo()
{
    if (Result is not null || _moves.Count == 0) return false;

    var removeFrom = Options.Mode == GameMode.LocalTwoPlayer
        ? _moves.Count - 1
        : _moves.FindLastIndex(item => item.Color == Options.HumanColor);

    if (removeFrom < 0) return false;
    _moves.RemoveRange(removeFrom, _moves.Count - removeFrom);
    _states.RemoveRange(removeFrom + 1, _states.Count - (removeFrom + 1));
    IsAiThinking = false;
    Revision++;
    return true;
}
```

`Resign(color)` sets the opponent as winner. Two passes leave `Result` unset and expose `State.HasTwoConsecutivePasses=true` so the scoring UI can begin. `RestoreAfterFinish()` clears `Result` and increments `Revision` without changing moves. `ResumeAfterScoringDispute()` replaces the current snapshot with an otherwise identical `BoardState` whose `ConsecutivePasses` is zero, increments `Revision`, and preserves every move.

- [ ] **Step 5: Run application tests and commit**

Run: `dotnet test tests/Yijing.Application.Tests/Yijing.Application.Tests.csproj`

Expected: PASS with 5 tests and 0 failures.

```powershell
git add src/Yijing.Application tests/Yijing.Application.Tests
git commit -m "feat: add game session undo and resignation"
```

---

### Task 5: 实现 SGF 主线读取与保存

**Files:**
- Create: `src/Yijing.Infrastructure/Sgf/SgfGame.cs`
- Create: `src/Yijing.Infrastructure/Sgf/SgfReader.cs`
- Create: `src/Yijing.Infrastructure/Sgf/SgfWriter.cs`
- Test: `tests/Yijing.Infrastructure.Tests/SgfReaderWriterTests.cs`

**Interfaces:**
- Consumes: `PlayedMove`, `GameOptions`, `GameResult`, `BoardPoint`.
- Produces: `SgfReader.Read(string) -> SgfGame` and `SgfWriter.Write(SgfGame) -> string`.

- [ ] **Step 1: Write SGF round-trip and variation tests**

Create `tests/Yijing.Infrastructure.Tests/SgfReaderWriterTests.cs`:

```csharp
using Yijing.Infrastructure.Sgf;

namespace Yijing.Infrastructure.Tests;

public sealed class SgfReaderWriterTests
{
    [Fact]
    public void Read_ParsesMetadataMovesPassAndResult()
    {
        const string text = "(;GM[1]FF[4]CA[UTF-8]AP[Yijing:1.0]SZ[9]KM[7.5]RU[Chinese]PB[玩家]PW[KataGo]RE[W+R];B[cc];W[];B[gg])";

        var game = SgfReader.Read(text);

        Assert.Equal(9, game.BoardSize);
        Assert.Equal(7.5, game.Komi);
        Assert.Equal("W+R", game.Result);
        Assert.Equal(3, game.Moves.Count);
        Assert.True(game.Moves[1].Move.Kind == Yijing.Domain.Board.MoveKind.Pass);
    }

    [Fact]
    public void Read_FollowsFirstVariationAndReportsBranches()
    {
        const string text = "(;GM[1]FF[4]SZ[9]KM[7.5];B[cc](;W[dd])(;W[ee]))";

        var game = SgfReader.Read(text);

        Assert.True(game.HasVariations);
        Assert.Equal(new Yijing.Domain.Board.BoardPoint(3, 3), game.Moves[1].Move.Point);
    }

    [Fact]
    public void WriteThenRead_PreservesMainLineAndEscapedNames()
    {
        var original = SgfGame.Create(9, 7.5, "张]三", "KataGo",
            [
                SgfMove.PlayBlack(2, 2),
                SgfMove.PassWhite()
            ], "B+1.5");

        var reparsed = SgfReader.Read(SgfWriter.Write(original));

        Assert.Equal(original.BlackName, reparsed.BlackName);
        Assert.Equal(original.Moves, reparsed.Moves);
        Assert.Equal("B+1.5", reparsed.Result);
    }
}
```

- [ ] **Step 2: Run tests and verify missing SGF types fail**

Run: `dotnet test tests/Yijing.Infrastructure.Tests/Yijing.Infrastructure.Tests.csproj --filter SgfReaderWriterTests`

Expected: FAIL with `CS0246` for `SgfReader`.

- [ ] **Step 3: Implement the SGF model and coordinate conversion**

Create `SgfGame` as an immutable record with `BoardSize`, `Komi`, `BlackName`, `WhiteName`, `IReadOnlyList<SgfMove> Moves`, nullable `Result`, and `HasVariations`. `SgfMove` stores `StoneColor Color` and `Move Move`. Convert SGF `aa` to `BoardPoint(0,0)`, reject coordinates outside `SZ`, and map an empty move value to `Move.Pass()`.

Use these exact entry points:

```csharp
public static class SgfReader
{
    public static SgfGame Read(string text);
}

public static class SgfWriter
{
    public static string Write(SgfGame game);
}
```

The reader tokenizes `(`, `)`, `;`, uppercase property identifiers, and bracket values. Within a bracket value, `\]` becomes `]`, `\\` becomes `\`, and escaped line endings are removed. It parses the root properties and follows the first child at every variation point while setting `HasVariations=true` if a node has more than one child.

- [ ] **Step 4: Implement deterministic UTF-8 SGF output**

Write properties in this order: `GM[1]FF[4]CA[UTF-8]AP[Yijing:1.0]SZ[...]KM[7.5]RU[Chinese]PB[...]PW[...]DT[yyyy-MM-dd]`, then `RE[...]` only when a result exists. Write every main-line move as `;B[xy]`, `;W[xy]`, `;B[]`, or `;W[]`. Escape backslashes before closing brackets.

- [ ] **Step 5: Run tests and commit**

Run: `dotnet test tests/Yijing.Infrastructure.Tests/Yijing.Infrastructure.Tests.csproj --filter SgfReaderWriterTests`

Expected: PASS with 3 tests and 0 failures.

```powershell
git add src/Yijing.Infrastructure/Sgf tests/Yijing.Infrastructure.Tests/SgfReaderWriterTests.cs
git commit -m "feat: add SGF main-line import and export"
```

---

### Task 6: 实现原子自动保存与恢复

**Files:**
- Create: `src/Yijing.Application/Persistence/IGameStore.cs`
- Create: `src/Yijing.Infrastructure/Storage/AtomicJsonStore.cs`
- Create: `src/Yijing.Infrastructure/Storage/LocalGameStore.cs`
- Test: `tests/Yijing.Infrastructure.Tests/AtomicJsonStoreTests.cs`
- Test: `tests/Yijing.Infrastructure.Tests/TemporaryDirectory.cs`

**Interfaces:**
- Consumes: `GameOptions`, `PlayedMove`, `GameResult`.
- Produces: `IGameStore.SaveAsync`, `LoadAsync`, `ClearAsync`; `%LocalAppData%\Yijing` layout.

- [ ] **Step 1: Write atomic replacement and recovery tests**

Create a test that writes snapshot A, overwrites it with snapshot B, deserializes B, and asserts no `.tmp` file remains. Add a second test asserting `LoadAsync` returns `null` when no file exists.

```csharp
[Fact]
public async Task SaveAsync_ReplacesExistingJsonAndRemovesTempFile()
{
    using var directory = new TemporaryDirectory();
    var store = new AtomicJsonStore(directory.Path);
    await store.WriteAsync("active-game.json", new { revision = 1 });
    await store.WriteAsync("active-game.json", new { revision = 2 });

    var value = await store.ReadAsync<Dictionary<string, int>>("active-game.json");

    Assert.Equal(2, value!["revision"]);
    Assert.False(File.Exists(Path.Combine(directory.Path, "active-game.json.tmp")));
}
```

Create `tests/Yijing.Infrastructure.Tests/TemporaryDirectory.cs`:

```csharp
namespace Yijing.Infrastructure.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "YijingTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}
```

- [ ] **Step 2: Run the test and verify it fails**

Run: `dotnet test tests/Yijing.Infrastructure.Tests/Yijing.Infrastructure.Tests.csproj --filter AtomicJsonStoreTests`

Expected: FAIL because `AtomicJsonStore` is missing.

- [ ] **Step 3: Implement atomic JSON IO**

`AtomicJsonStore.WriteAsync` creates the directory, serializes UTF-8 JSON to `<name>.tmp` using `FileOptions.WriteThrough`, flushes, then calls `File.Move(temp, target, true)`. `ReadAsync<T>` opens with `FileShare.Read`, returns `null` for a missing file, and propagates `JsonException` so the application can display a corrupt-recovery warning.

Define the application contract:

```csharp
public sealed record GameSnapshotDto(GameOptions Options,
    IReadOnlyList<PlayedMove> Moves, GameResult? Result, long Revision);

public interface IGameStore
{
    Task SaveAsync(GameSnapshotDto snapshot, CancellationToken cancellationToken);
    Task<GameSnapshotDto?> LoadAsync(CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}
```

`LocalGameStore` stores the active snapshot at `%LocalAppData%\Yijing\autosave\active-game.json`; settings and engine profiles use sibling files `settings.json` and `engine-profile.json`.

- [ ] **Step 4: Run tests and commit**

Run: `dotnet test tests/Yijing.Infrastructure.Tests/Yijing.Infrastructure.Tests.csproj --filter AtomicJsonStoreTests`

Expected: PASS with all atomic-store tests.

```powershell
git add src/Yijing.Application/Persistence src/Yijing.Infrastructure/Storage tests/Yijing.Infrastructure.Tests/AtomicJsonStoreTests.cs
git commit -m "feat: add atomic game autosave"
```

---

### Task 7: 实现 KataGo JSON 客户端与 30 秒极限搜索

**Files:**
- Create: `src/Yijing.Application/Analysis/AnalysisContracts.cs`
- Create: `src/Yijing.Application/Analysis/AnalysisCoordinator.cs`
- Create: `src/Yijing.Infrastructure/KataGo/KataGoDtos.cs`
- Create: `src/Yijing.Infrastructure/KataGo/IKataGoTransport.cs`
- Create: `src/Yijing.Infrastructure/KataGo/ProcessKataGoTransport.cs`
- Create: `src/Yijing.Infrastructure/KataGo/KataGoAnalysisClient.cs`
- Test: `tests/Yijing.Infrastructure.Tests/KataGoAnalysisClientTests.cs`
- Test: `tests/Yijing.Application.Tests/AnalysisCoordinatorTests.cs`

**Interfaces:**
- Consumes: full move history, board size, next player, rules and `GameSession.Revision`.
- Produces: `IPositionAnalyzer.AnalyzeAsync`, partial `AnalysisResult`, and `AnalysisCoordinator.FindAiMoveAsync`.

- [ ] **Step 1: Write protocol correlation and terminate tests**

Use an in-memory `FakeKataGoTransport` whose writes are recorded and whose reads come from a `Channel<string>`. Submit request `game-7`, enqueue a partial response, call `TerminateAsync("game-7")`, enqueue a final response, and assert the client returns final `Q16` while writing:

```json
{"id":"stop-game-7","action":"terminate","terminateId":"game-7"}
```

Also enqueue results for `game-8` before `game-7` and assert each task receives only its matching ID.

- [ ] **Step 2: Run protocol tests and verify they fail**

Run: `dotnet test tests/Yijing.Infrastructure.Tests/Yijing.Infrastructure.Tests.csproj --filter KataGoAnalysisClientTests`

Expected: FAIL because `KataGoAnalysisClient` is missing.

- [ ] **Step 3: Implement exact JSON contracts and line transport**

Use `JsonPropertyName` attributes for KataGo names. Requests contain `id`, `moves`, `initialPlayer`, `rules="chinese"`, `komi=7.5`, `boardXSize`, `boardYSize`, `maxVisits=100000000`, `analysisPVLen=12`, and `reportDuringSearchEvery=1.0`. Responses expose `id`, `isDuringSearch`, `turnNumber`, `moveInfos`, `rootInfo`, `error`, and `warning`.

Define:

```csharp
public interface IKataGoTransport : IAsyncDisposable
{
    ValueTask WriteLineAsync(string line, CancellationToken cancellationToken);
    IAsyncEnumerable<string> ReadLinesAsync(CancellationToken cancellationToken);
}

public sealed record CandidateMove(string Move, double Winrate, double ScoreLead, int Visits);
public sealed record AnalysisResult(string RequestId, bool IsFinal,
    IReadOnlyList<CandidateMove> Candidates, double RootWinrate, double RootScoreLead);

public interface IPositionAnalyzer
{
    IAsyncEnumerable<AnalysisResult> AnalyzeAsync(
        AnalysisPosition position, string requestId, CancellationToken cancellationToken);
    Task TerminateAsync(string requestId, CancellationToken cancellationToken);
}

public sealed record AnalysisPosition(int BoardSize, StoneColor NextPlayer,
    IReadOnlyList<PlayedMove> Moves, double Komi, long GameRevision);
```

`KataGoAnalysisClient` starts one read loop, deserializes each line, and routes it through a `ConcurrentDictionary<string, RequestState>`. A final response completes the request. `TerminateAsync` writes the exact terminate JSON and keeps the original request registered until its final response arrives.

- [ ] **Step 4: Implement the 30-second coordinator with injectable duration**

`AnalysisCoordinator` accepts `IPositionAnalyzer`, a search duration, and an `IProgress<AnalysisResult>`. Its production composition passes `TimeSpan.FromSeconds(30)`. It submits the current full position, publishes partial results only while the captured revision equals `GameSession.Revision`, waits `Task.Delay(duration, cancellationToken)`, sends terminate, and selects the first candidate that `GoRules.TryApply` accepts. Cancellation caused by play, undo, SGF load, or exit terminates the active request.

Test with `TimeSpan.FromMilliseconds(25)` and a fake analyzer. Assert one terminate call, the first legal final candidate, and zero progress publications after revision changes.

- [ ] **Step 5: Run analysis tests and commit**

Run: `dotnet test tests/Yijing.Infrastructure.Tests/Yijing.Infrastructure.Tests.csproj --filter KataGoAnalysisClientTests; dotnet test tests/Yijing.Application.Tests/Yijing.Application.Tests.csproj --filter AnalysisCoordinatorTests`

Expected: both commands PASS.

```powershell
git add src/Yijing.Application/Analysis src/Yijing.Infrastructure/KataGo tests/Yijing.Application.Tests/AnalysisCoordinatorTests.cs tests/Yijing.Infrastructure.Tests/KataGoAnalysisClientTests.cs
git commit -m "feat: integrate KataGo JSON analysis protocol"
```

---

### Task 8: 实现引擎资源清单、自动检测与回退

**Files:**
- Create: `src/Yijing.Infrastructure/KataGo/EngineManifest.cs`
- Create: `src/Yijing.Infrastructure/KataGo/EngineCandidate.cs`
- Create: `src/Yijing.Infrastructure/KataGo/BackendSelector.cs`
- Create: `assets/katago/engine-manifest.json`
- Create: `assets/katago/analysis.cfg`
- Create: `scripts/Fetch-KataGoAssets.ps1`
- Create: `scripts/Verify-KataGoAssets.ps1`
- Test: `tests/Yijing.Infrastructure.Tests/BackendSelectorTests.cs`

**Interfaces:**
- Consumes: packaged executables, model files, CPU AVX2 capability, cached profile.
- Produces: one verified `EngineCandidate` and a persisted `EngineProfile`.

- [ ] **Step 1: Write candidate ordering and fallback tests**

Test these cases with a fake probe:

1. Cached OpenCL succeeds: it is returned without probing Eigen.
2. TensorRT and OpenCL fail, Eigen AVX2 succeeds: AVX2 is returned.
3. AVX2 is unsupported: generic Eigen is probed.
4. SHA-256 mismatch: the candidate is rejected before process launch.

- [ ] **Step 2: Run tests and verify they fail**

Run: `dotnet test tests/Yijing.Infrastructure.Tests/Yijing.Infrastructure.Tests.csproj --filter BackendSelectorTests`

Expected: FAIL because `BackendSelector` is missing.

- [ ] **Step 3: Implement manifest and selector policy**

Manifest entries contain `Name`, `Backend`, `Executable`, `Model`, `Config`, `Sha256`, `RequiresAvx2`, and `Priority`. Order candidates by a successful cached profile first, then TensorRT, OpenCL, Eigen AVX2, and generic Eigen. For each candidate: verify every digest, skip AVX2 when `System.Runtime.Intrinsics.X86.Avx2.IsSupported` is false, run `katago benchmark` with a 90-second startup limit, and require exit code 0 plus a positive visits-per-second value. Return the highest visits-per-second candidate within the first successful backend class.

Pin the first release manifest to KataGo v1.17.x with GPU model `b11c768h12nbt3tflrs-fson-silu.bin.gz` and CPU model `b10c384h6nbttflrs.bin.gz`. The fetch script downloads only official GitHub release assets and official release models, computes SHA-256 with `Get-FileHash`, and writes the final digest into `engine-manifest.json`. The verify script exits nonzero for a missing or mismatched file.

- [ ] **Step 4: Add a deterministic analysis configuration**

Set `numAnalysisThreads=1`, `numSearchThreadsPerAnalysisThread` from the selected benchmark, `nnMaxBatchSize` from the candidate profile, `reportAnalysisWinratesAs=SIDETOMOVE`, `logToStderr=true`, and place writable tuning/cache files under `%LocalAppData%\Yijing\engine-cache`, never under `Program Files`.

- [ ] **Step 5: Run tests and asset validation, then commit**

Run:

```powershell
dotnet test tests/Yijing.Infrastructure.Tests/Yijing.Infrastructure.Tests.csproj --filter BackendSelectorTests
powershell -ExecutionPolicy Bypass -File scripts/Verify-KataGoAssets.ps1 -Manifest assets/katago/engine-manifest.json -AllowMissingDownloadedAssets
```

Expected: tests PASS; the validation script exits 0 while reporting that download-only assets are absent in the source checkout.

```powershell
git add src/Yijing.Infrastructure/KataGo assets/katago scripts tests/Yijing.Infrastructure.Tests/BackendSelectorTests.cs
git commit -m "feat: add automatic KataGo backend selection"
```

---

### Task 9: 实现 MVVM 基础与对局视图模型

**Files:**
- Create: `src/Yijing.Desktop/ViewModels/ObservableObject.cs`
- Create: `src/Yijing.Desktop/ViewModels/RelayCommand.cs`
- Create: `src/Yijing.Desktop/ViewModels/GameViewModel.cs`
- Create: `src/Yijing.Desktop/ViewModels/NewGameViewModel.cs`
- Create: `src/Yijing.Desktop/Services/IDialogService.cs`
- Test: `tests/Yijing.Desktop.Tests/GameViewModelTests.cs`

**Interfaces:**
- Consumes: `GameSession`, `AnalysisCoordinator`, `IGameStore`.
- Produces: bindable board state, player cards, analysis values, `PlayCommand`, `UndoCommand`, `PassCommand`, `ResignCommand`.

- [ ] **Step 1: Write view-model command tests**

Create `GameViewModelTests` with fakes for analysis, persistence, and dialogs. Verify:

- a legal board click updates `State`, increments `Revision`, and saves once;
- an occupied click does not save;
- `UndoCommand` cancels analysis, calls `GameSession.Undo`, and saves;
- `ResignCommand` asks for confirmation and stores `W+R` when the human is black;
- `IsAnalysisVisible=false` clears displayed candidates without stopping the game.

- [ ] **Step 2: Run tests and verify they fail**

Run: `dotnet test tests/Yijing.Desktop.Tests/Yijing.Desktop.Tests.csproj --filter GameViewModelTests`

Expected: FAIL because `GameViewModel` is missing.

- [ ] **Step 3: Implement notification and command primitives**

`ObservableObject.SetProperty<T>` compares with `EqualityComparer<T>.Default`, writes the backing field, and raises `PropertyChanged`. `RelayCommand` and `AsyncRelayCommand` implement `ICommand`, honor a supplied `CanExecute`, prevent concurrent async execution, and expose `NotifyCanExecuteChanged()`.

Create `IDialogService` with this surface; Task 11 supplies the WPF implementation:

```csharp
public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message);
    Task ShowMessageAsync(string title, string message);
}
```

- [ ] **Step 4: Implement `GameViewModel`**

Expose these stable bindings:

```csharp
public BoardState State { get; }
public IReadOnlyList<CandidateMove> Candidates { get; }
public string WinrateText { get; }
public string ScoreLeadText { get; }
public string TurnText { get; }
public string EngineStatusText { get; }
public bool IsAnalysisVisible { get; set; }
public bool IsInputEnabled { get; }
public ICommand PlayCommand { get; }
public ICommand UndoCommand { get; }
public ICommand PassCommand { get; }
public ICommand ResignCommand { get; }
```

`PlayCommand` accepts a `BoardPoint`; after every legal play, pass, AI move, or undo, await `IGameStore.SaveAsync`. When the next player is AI, disable board input and start the 30-second search. The resignation confirmation text is `确定认输吗？本局将立即结束。` and the affirmative action calls `GameSession.Resign(humanColor)`.

- [ ] **Step 5: Implement `NewGameViewModel` validation**

Allowed sizes are exactly `[19, 13, 9]`. Human-vs-AI exposes black, white, and random; random uses `RandomNumberGenerator.GetInt32(2)` so the result is unbiased. Local two-player hides color choice. `CreateOptions()` always returns `Komi=7.5`.

- [ ] **Step 6: Run view-model tests and commit**

Run: `dotnet test tests/Yijing.Desktop.Tests/Yijing.Desktop.Tests.csproj --filter GameViewModelTests`

Expected: PASS.

```powershell
git add src/Yijing.Desktop/ViewModels tests/Yijing.Desktop.Tests/GameViewModelTests.cs
git commit -m "feat: add desktop game view models"
```

---

### Task 10: 构建专业分析台和可缩放棋盘

**Files:**
- Modify: `src/Yijing.Desktop/App.xaml`
- Modify: `src/Yijing.Desktop/MainWindow.xaml`
- Modify: `src/Yijing.Desktop/MainWindow.xaml.cs`
- Create: `src/Yijing.Desktop/Controls/BoardRenderPalette.cs`
- Create: `src/Yijing.Desktop/Controls/GoBoardControl.cs`
- Create: `src/Yijing.Desktop/Views/NewGameDialog.xaml`
- Create: `src/Yijing.Desktop/Views/NewGameDialog.xaml.cs`
- Create: `src/Yijing.Desktop/Converters/StoneConverters.cs`

**Interfaces:**
- Consumes: Task 9 bindings.
- Produces: dark professional workstation, square board control, new-game dialog and keyboard-accessible controls.

- [ ] **Step 1: Add a board geometry test seam**

Extract pure methods from `GoBoardControl`:

```csharp
public static Point PointToPixel(BoardPoint point, int size, Rect bounds);
public static BoardPoint? PixelToPoint(Point pixel, int size, Rect bounds, double tolerance);
```

Write tests for corners, center, 125% scale, and a click outside tolerance. Run them first and expect missing-method compilation failures.

- [ ] **Step 2: Implement board rendering**

In `OnRender`, calculate a centered square with 28 device-independent pixel padding, draw warm wood `#D5A45C`, draw `size` horizontal and vertical lines, star points for each supported size, optional coordinates excluding the letter I, then stones with radial brushes. Draw the latest move as a 6-pixel contrasting ring and up to three candidates as numbered translucent teal circles. Set `SnapsToDevicePixels=true` and use `VisualTreeHelper.GetDpi(this)` when aligning one-pixel grid lines.

On left mouse release, convert to `BoardPoint` with a tolerance of 45% of grid spacing and execute the bound `PlayCommand` only when `IsInputEnabled=true`.

- [ ] **Step 3: Implement the main XAML layout**

Use a two-row `Grid`: toolbar at row 0, content at row 1. Content uses `GridLength="*"` for the board and fixed `280` for the right analysis rail. The rail contains player cards, winrate and score lead, candidate list, game metadata, then bottom buttons `悔棋`, `停一手`, `认输`. Bind `认输` to `ResignCommand`, give it `AutomationProperties.Name="认输"`, and style it with a muted red foreground. Minimum window size is 1024×720.

Define theme resources in `App.xaml`: background `#101817`, surface `#172220`, border `#2B3B37`, primary `#70DDB6`, wood `#D5A45C`, text `#E9F0ED`, muted text `#A9B9B4`.

- [ ] **Step 4: Implement new-game dialog**

The dialog contains mode radio buttons, color buttons, board-size buttons, and `开始对局`/`取消`. It contains no difficulty control. Selecting white results in an AI opening move before player input becomes enabled.

- [ ] **Step 5: Build and manually inspect DPI layouts**

Run:

```powershell
dotnet build src/Yijing.Desktop/Yijing.Desktop.csproj -c Debug
dotnet run --project src/Yijing.Desktop/Yijing.Desktop.csproj
```

Expected: the app opens at 1280×820; board stays square while resizing; every toolbar and action button is reachable; no clipping occurs at 125%, 150%, or 200% display scaling.

- [ ] **Step 6: Commit the UI shell**

```powershell
git add src/Yijing.Desktop tests/Yijing.Desktop.Tests
git commit -m "feat: build professional Go desktop interface"
```

---

### Task 11: 接通终局数子、恢复、错误提示和日志

**Files:**
- Create: `src/Yijing.Desktop/Views/ScoringDialog.xaml`
- Create: `src/Yijing.Desktop/Views/ScoringDialog.xaml.cs`
- Create: `src/Yijing.Desktop/Services/DialogService.cs`
- Create: `src/Yijing.Desktop/ViewModels/MainWindowViewModel.cs`
- Modify: `src/Yijing.Desktop/App.xaml.cs`
- Modify: `src/Yijing.Desktop/ViewModels/GameViewModel.cs`
- Modify: `src/Yijing.Infrastructure/Storage/LocalGameStore.cs`
- Test: `tests/Yijing.Desktop.Tests/GameViewModelTests.cs`

**Interfaces:**
- Consumes: `ChineseAreaScorer`, autosave, engine startup and ownership suggestions.
- Produces: complete start-to-finish game flow and recoverable failure states.

- [ ] **Step 1: Add failing endgame and restore tests**

Test that two consecutive passes open scoring, toggling one stone toggles its whole connected group, confirming score writes `B+1.5` or `W+2.5`, and choosing `继续对局` clears consecutive passes without deleting moves. Test that startup with an autosave asks `恢复上次未完成的对局吗？` and reconstructs the same board by replaying every move through `GoRules`.

- [ ] **Step 2: Implement scoring interaction**

Initialize dead stones from KataGo ownership values whose absolute confidence is at least `0.95` and whose predicted owner opposes the stone color. Treat this only as a suggestion. Clicking a stone flood-fills its connected group and toggles every point. Recompute `ChineseAreaScorer.Score` after each toggle. Confirming assigns `GameResult` with `GameEndReason.Score`; continuing closes the dialog, resets pass count through a domain method, and resumes the same game.

- [ ] **Step 3: Implement startup recovery and engine states**

`App.xaml.cs` builds services, attempts autosave load, and shows the restore prompt before creating a blank session. It starts `BackendSelector` on a background task and publishes these Chinese states: `正在检测 AI`, `AI 准备完成`, `GPU 不可用，已切换 CPU`, and `AI 暂不可用，可进行本地双人对局`.

If KataGo exits unexpectedly, save the current snapshot, restart once, replay the full history, and resume. A second failure in the same session disables AI commands while leaving SGF and local play enabled.

- [ ] **Step 4: Implement bounded rolling logs**

Write logs under `%LocalAppData%\Yijing\logs`. Rotate when `yijing.log` exceeds 5 MB, keep seven dated files, and delete older files during startup. Never log SGF player names or full move history; log engine version, backend, exit code, request ID, timing, and exception type.

- [ ] **Step 5: Run the full test suite and commit**

Run: `dotnet test Yijing.sln -c Release`

Expected: all tests PASS with 0 failures.

```powershell
git add src tests
git commit -m "feat: complete scoring recovery and diagnostics"
```

---

### Task 12: 打包离线安装程序并执行验收

**Files:**
- Create: `tools/FakeKataGo/FakeKataGo.csproj`
- Create: `tools/FakeKataGo/Program.cs`
- Create: `scripts/Run-Acceptance.ps1`
- Create: `packaging/Yijing.iss`
- Create: `packaging/THIRD-PARTY-NOTICES.txt`
- Modify: `src/Yijing.Desktop/Yijing.Desktop.csproj`
- Modify: `.gitignore`

**Interfaces:**
- Consumes: release-published desktop app and verified KataGo assets.
- Produces: `artifacts/installer/Yijing-Setup-x64.exe` and repeatable acceptance evidence.

- [ ] **Step 1: Build a deterministic fake engine for installer smoke tests**

`FakeKataGo` reads one JSON object per line. For analysis requests it emits a partial result with `D4`, waits 50 ms, then emits a final result with `Q16`. For terminate it echoes the action. For `--crash-after-one` it exits with code 23 after its first response. Use it to run UI smoke tests without GPU hardware.

- [ ] **Step 2: Configure self-contained publishing**

Add to `Yijing.Desktop.csproj`:

```xml
<PropertyGroup>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <SelfContained>true</SelfContained>
  <PublishSingleFile>false</PublishSingleFile>
  <PublishReadyToRun>true</PublishReadyToRun>
  <ApplicationManifest>app.manifest</ApplicationManifest>
</PropertyGroup>
```

Publish with:

```powershell
dotnet publish src/Yijing.Desktop/Yijing.Desktop.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
```

Expected: `artifacts/publish/win-x64/Yijing.Desktop.exe` starts on a machine with no .NET SDK.

- [ ] **Step 3: Create the Inno Setup installer**

`Yijing.iss` uses `ArchitecturesAllowed=x64compatible`, installs under `{autopf}\Yijing`, copies the published app and verified `assets\katago` tree, creates Start Menu and optional desktop shortcuts, preserves `%LocalAppData%\Yijing` on upgrade/uninstall, and displays `THIRD-PARTY-NOTICES.txt`. Output filename is `Yijing-Setup-x64.exe`.

Run:

```powershell
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" packaging\Yijing.iss
```

Expected: `artifacts/installer/Yijing-Setup-x64.exe` exists and is larger than the combined model payload.

- [ ] **Step 4: Automate acceptance checks**

`Run-Acceptance.ps1` must execute and record these checks:

1. `dotnet test Yijing.sln -c Release` passes.
2. Resource digest verification passes.
3. Fake engine produces a legal AI response and terminate acknowledgement.
4. A scripted 9×9 game saves SGF, reloads it, and produces the same final board key.
5. Human-black and human-white undo scenarios return to the human turn.
6. Resignation produces `W+R` or `B+R`.
7. Corrupt model selection falls back to Eigen.
8. A killed app restores the last autosaved revision.
9. Publish output contains no development-only files or user data.

The script writes `artifacts/acceptance/results.json` with one named boolean per check and exits 1 if any value is false.

- [ ] **Step 5: Run release verification**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Run-Acceptance.ps1
git status --short
```

Expected: nine acceptance checks are `true`; Git status lists only intentionally generated ignored artifacts.

- [ ] **Step 6: Commit packaging and release automation**

```powershell
git add src/Yijing.Desktop/Yijing.Desktop.csproj tools scripts packaging .gitignore
git commit -m "build: add offline Windows installer and acceptance suite"
```

---

## Final Verification

Run:

```powershell
dotnet test Yijing.sln -c Release
dotnet publish src/Yijing.Desktop/Yijing.Desktop.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
powershell -ExecutionPolicy Bypass -File scripts/Verify-KataGoAssets.ps1 -Manifest assets/katago/engine-manifest.json
powershell -ExecutionPolicy Bypass -File scripts/Run-Acceptance.ps1
```

Expected:

- every unit, component, and view-model test passes;
- all KataGo executables and models match the committed manifest;
- all nine acceptance checks are true;
- the self-contained app launches without a system .NET installation;
- the installer contains the WPF app, OpenCL/Eigen fallback, selected Transformer models, configuration, and notices.
