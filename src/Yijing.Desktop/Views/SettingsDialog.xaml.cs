using System.Windows;
using Yijing.Desktop.ViewModels;

namespace Yijing.Desktop.Views;

public partial class SettingsDialog : Window
{
    public SettingsDialog(SettingsViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;
        InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        ViewModel.Apply();
        if (ViewModel.ErrorText == "") DialogResult = true;
    }
}
