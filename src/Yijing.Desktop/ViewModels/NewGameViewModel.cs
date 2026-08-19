using System.Security.Cryptography;
using Yijing.Application.Games;
using Yijing.Domain.Board;

namespace Yijing.Desktop.ViewModels;

public enum HumanColorChoice { Black, White, Random }

public sealed class NewGameViewModel : ObservableObject
{
    public static IReadOnlyList<int> AvailableBoardSizes { get; } = [19, 13, 9];
    public static IReadOnlyList<HumanColorChoice> AvailableHumanColors { get; } =
        [HumanColorChoice.Black, HumanColorChoice.White, HumanColorChoice.Random];

    private GameMode _mode = GameMode.HumanVsAi;
    private int _boardSize = 19;
    private HumanColorChoice _humanColorChoice = HumanColorChoice.Black;

    public GameMode Mode
    {
        get => _mode;
        set
        {
            if (!SetProperty(ref _mode, value)) return;
            OnPropertyChanged(nameof(IsColorChoiceVisible));
        }
    }

    public int BoardSize
    {
        get => _boardSize;
        set
        {
            if (!AvailableBoardSizes.Contains(value))
                throw new ArgumentOutOfRangeException(nameof(value), "棋盘尺寸仅支持 19、13 或 9 路。");
            SetProperty(ref _boardSize, value);
        }
    }

    public HumanColorChoice HumanColorChoice
    {
        get => _humanColorChoice;
        set => SetProperty(ref _humanColorChoice, value);
    }

    public bool IsColorChoiceVisible => Mode == GameMode.HumanVsAi;

    public GameOptions CreateOptions()
    {
        StoneColor? humanColor = Mode == GameMode.LocalTwoPlayer ? null : HumanColorChoice switch
        {
            HumanColorChoice.Black => StoneColor.Black,
            HumanColorChoice.White => StoneColor.White,
            HumanColorChoice.Random => RandomNumberGenerator.GetInt32(2) == 0 ? StoneColor.Black : StoneColor.White,
            _ => throw new InvalidOperationException("无效的执棋颜色选择。")
        };
        return new GameOptions(Mode, BoardSize, humanColor, 7.5);
    }
}
