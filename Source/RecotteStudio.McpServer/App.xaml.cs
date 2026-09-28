using Microsoft.UI.Xaml;

namespace RecotteStudio.McpServer.UI;

public partial class App : Application
{
    private Mutex? instanceMutex;
    private MainWindow? window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        instanceMutex = new Mutex(true, @"Local\RecotteStudioMCP", out bool firstInstance);
        if (!firstInstance)
        {
            instanceMutex.Dispose();
            Environment.Exit(0);
            return;
        }

        window = new MainWindow();
        window.Activate();
        _ = window.StartAsync();
    }
}
