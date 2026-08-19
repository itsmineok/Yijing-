using System.Windows;
using Yijing.Application.Games;
using Yijing.Desktop.ViewModels;

namespace Yijing.Desktop.Views;

public partial class NewGameDialog : Window
{
    public NewGameDialog() : this(new NewGameViewModel())
    {
    }

    public NewGameDialog(NewGameViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;
        InitializeComponent();
    }

    public NewGameViewModel ViewModel { get; }
    public GameOptions? Options { get; private set; }

    private void OnStartClick(object sender, RoutedEventArgs e)
    {
        Options = ViewModel.CreateOptions();
        DialogResult = true;
    }
}
