using System.ComponentModel;
using System.Windows;
using Yijing.Application.Games;
using Yijing.Desktop.ViewModels;
using Yijing.Desktop.Views;

namespace Yijing.Desktop;

public partial class MainWindow : Window
{
    private bool _shutdownStarted;
    private bool _shutdownComplete;

    public MainWindow()
    {
        InitializeComponent();
    }

    public event Action<GameOptions>? NewGameRequested;

    public event Func<SettingsViewModel>? SettingsRequested;

    private void OnNewGameClick(object sender, RoutedEventArgs e)
    {
        var dialog = new NewGameDialog { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Options is { } options)
            NewGameRequested?.Invoke(options);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        if (SettingsRequested is null) return;
        var viewModel = SettingsRequested();
        var dialog = new SettingsDialog(viewModel) { Owner = this };
        dialog.ShowDialog();
    }

    private async void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_shutdownComplete || DataContext is not IAsyncDisposable disposable) return;
        e.Cancel = true;
        if (_shutdownStarted) return;

        _shutdownStarted = true;
        IsEnabled = false;
        try
        {
            await disposable.DisposeAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"关闭对局时发生错误：{exception.Message}", "弈境",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _shutdownComplete = true;
            await Dispatcher.InvokeAsync(Close, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }
}
