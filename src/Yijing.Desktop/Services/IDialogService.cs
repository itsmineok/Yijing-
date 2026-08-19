namespace Yijing.Desktop.Services;

using Yijing.Desktop.ViewModels;

public enum ScoringDialogOutcome { ConfirmScore, ContinueGame }

public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message);
    Task ShowMessageAsync(string title, string message);
    Task<ScoringDialogOutcome> ShowScoringAsync(
        ScoringViewModel scoring,
        CancellationToken cancellationToken = default);
}
