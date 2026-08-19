using Yijing.Application.Analysis;
using Yijing.Infrastructure.KataGo;

namespace Yijing.Desktop.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly Action<AnalysisTimeSettings> _apply;
    private readonly Func<Task<EngineSelection>>? _rebenchmark;

    private double _openingSeconds;
    private double _maxSeconds;
    private string _errorText = "";
    private string _rebenchmarkStatusText = "";
    private bool _isRebenchmarking;
    private AnalysisTimeSettings? _applied;

    public SettingsViewModel(
        AnalysisTimeSettings current,
        Action<AnalysisTimeSettings> apply,
        Func<Task<EngineSelection>>? rebenchmark = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(apply);
        _apply = apply;
        _rebenchmark = rebenchmark;
        _openingSeconds = current.OpeningSeconds;
        _maxSeconds = current.MaxSeconds;
        ApplyCommand = new RelayCommand(_ => Apply());
        RebenchmarkCommand = new AsyncRelayCommand(
            async _ => await RebenchmarkAsync().ConfigureAwait(false),
            _ => _rebenchmark is not null);
    }

    public double OpeningSeconds
    {
        get => _openingSeconds;
        set => SetProperty(ref _openingSeconds, value);
    }

    public double MaxSeconds
    {
        get => _maxSeconds;
        set => SetProperty(ref _maxSeconds, value);
    }

    public string ErrorText
    {
        get => _errorText;
        private set => SetProperty(ref _errorText, value);
    }

    public string RebenchmarkStatusText
    {
        get => _rebenchmarkStatusText;
        private set => SetProperty(ref _rebenchmarkStatusText, value);
    }

    public bool IsRebenchmarking
    {
        get => _isRebenchmarking;
        private set
        {
            if (!SetProperty(ref _isRebenchmarking, value)) return;
            RebenchmarkCommand.NotifyCanExecuteChanged();
        }
    }

    public RelayCommand ApplyCommand { get; }

    public AsyncRelayCommand RebenchmarkCommand { get; }

    public AnalysisTimeSettings? Applied => _applied;

    public void Apply()
    {
        try
        {
            var settings = new AnalysisTimeSettings(OpeningSeconds, MaxSeconds);
            _applied = settings;
            ErrorText = "";
            _apply(settings);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            ErrorText = exception.Message;
        }
    }

    private async Task RebenchmarkAsync()
    {
        if (_rebenchmark is null) return;
        IsRebenchmarking = true;
        RebenchmarkStatusText = "正在基准测试各引擎后端，TensorRT 首次运行可能需要几分钟…";
        try
        {
            var selection = await _rebenchmark().ConfigureAwait(false);
            RebenchmarkStatusText =
                $"已选择 {selection.Candidate.Name}（{selection.Profile.VisitsPerSecond:0} visits/s）";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            RebenchmarkStatusText = $"基准测试失败：{exception.Message}";
        }
        finally
        {
            IsRebenchmarking = false;
        }
    }
}
