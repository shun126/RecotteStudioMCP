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
        ActivityList.ItemsSource = owner.Activity;
        owner.ActivityChanged += RefreshActivity;
        owner.ActivityPulsed += Pulse;
        Refresh();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (owner is null) return;
        owner.ActivityChanged -= RefreshActivity;
        owner.ActivityPulsed -= Pulse;
    }

    internal void Refresh()
    {
        EndpointBox.Text = owner?.ServerEndpoint?.ToString() ?? "http://127.0.0.1:8765/mcp";
        WorkspaceText.Text = owner?.WorkspaceRoot ?? "制限なし";
        RefreshActivity();
    }

    private void RefreshActivity()
    {
        if (owner is null) return;
        bool running = owner.ServerEndpoint is not null;
        LampOn.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        LampOff.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        LampTitle.Text = !running ? "停止中" : owner.RunningCalls > 0 ? "処理中" : "待機中";

        string calls = $"ツール呼び出し {owner.ToolCallCount} 回";
        LampDetail.Text = owner.LastAccess is DateTimeOffset last
            ? $"最終アクセス {last:HH:mm:ss} ・ {calls}"
            : running ? "要求を受け付けています。まだアクセスはありません" : "サーバーは動作していません";

        bool empty = owner.Activity.Count == 0;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ActivityList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        ClearButton.IsEnabled = !empty;
    }

    private void Pulse()
    {
        PulseStoryboard.Stop();
        PulseStoryboard.Begin();
        RefreshActivity();
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => owner?.ClearActivity();

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => owner?.OpenSettings();
}
