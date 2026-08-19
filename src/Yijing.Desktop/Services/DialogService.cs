using System.Windows;
using Yijing.Desktop.ViewModels;
using Yijing.Desktop.Views;

namespace Yijing.Desktop.Services;

public sealed class DialogService(Func<Window?>? ownerProvider = null) : IDialogService
{
    private readonly Func<Window?> _ownerProvider = ownerProvider ?? (() => System.Windows.Application.Current?.MainWindow);

    public Task<bool> ConfirmAsync(string title, string message)
    {
        var owner = _ownerProvider();
        var result = owner is null
            ? MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            : MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        return Task.FromResult(result == MessageBoxResult.Yes);
    }

    public Task ShowMessageAsync(string title, string message)
    {
        var owner = _ownerProvider();
        if (owner is null)
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        else
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        return Task.CompletedTask;
    }

    public Task<ScoringDialogOutcome> ShowScoringAsync(
        ScoringViewModel scoring,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new ScoringDialog(scoring);
        if (_ownerProvider() is { } owner) dialog.Owner = owner;
        dialog.ShowDialog();
        return Task.FromResult(dialog.Outcome);
    }
}
