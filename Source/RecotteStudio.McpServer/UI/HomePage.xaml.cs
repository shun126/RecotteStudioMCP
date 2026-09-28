using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace RecotteStudio.McpServer.UI;

public sealed partial class HomePage : Page
{
    private MainWindow? owner;

    public HomePage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        owner = (MainWindow)e.Parameter;
        Refresh();
    }

    internal void Refresh()
    {
        EndpointBox.Text = owner?.ServerEndpoint?.ToString() ?? "http://127.0.0.1:8765/mcp";
        WorkspaceText.Text = owner?.WorkspaceRoot ?? "制限なし";
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => owner?.OpenSettings();
}
