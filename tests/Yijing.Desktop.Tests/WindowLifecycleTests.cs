using System.Windows.Threading;

namespace Yijing.Desktop.Tests;

public sealed class WindowLifecycleTests
{
    [Fact]
    public async Task Window_closes_cleanly_when_view_model_disposal_completes_synchronously()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                var application = new App();
                application.InitializeComponent();
                dispatcher.UnhandledException += (_, args) =>
                {
                    args.Handled = true;
                    completion.TrySetException(args.Exception);
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                };
                var window = new MainWindow { DataContext = new ImmediateDisposable() };
                window.Closed += (_, _) =>
                {
                    completion.TrySetResult();
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                };
                window.Loaded += (_, _) => window.Close();
                window.Show();
                Dispatcher.Run();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(thread.Join(TimeSpan.FromSeconds(1)));
    }

    private sealed class ImmediateDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
