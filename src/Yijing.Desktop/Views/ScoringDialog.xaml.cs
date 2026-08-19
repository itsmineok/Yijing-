using System.Windows;
using Yijing.Desktop.Services;
using Yijing.Desktop.ViewModels;

namespace Yijing.Desktop.Views;

public partial class ScoringDialog : Window
{
    public ScoringDialog(ScoringViewModel scoring)
    {
        InitializeComponent();
        DataContext = scoring ?? throw new ArgumentNullException(nameof(scoring));
    }

    public ScoringDialogOutcome Outcome { get; private set; } = ScoringDialogOutcome.ContinueGame;

    private void OnContinueClick(object sender, RoutedEventArgs e)
    {
        Outcome = ScoringDialogOutcome.ContinueGame;
        DialogResult = true;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        Outcome = ScoringDialogOutcome.ConfirmScore;
        DialogResult = true;
    }
}
