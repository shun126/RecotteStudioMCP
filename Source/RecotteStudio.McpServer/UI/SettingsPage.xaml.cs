using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace RecotteStudio.McpServer.UI;

public sealed partial class SettingsPage : Page
{
    private MainWindow? owner;

    public SettingsPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        owner = (MainWindow)e.Parameter;
        Refresh();
    }

    internal void Refresh()
    {
        if (owner is null) return;
        WorkspaceBox.Text = owner.WorkspaceRoot ?? string.Empty;
        if (owner.AccessToken is string token)
        {
            CodexBox.Text = ConnectionSnippets.Codex(token);
            ClaudeBox.Text = ConnectionSnippets.Claude(token);
        }
    }

    private async void ApplyWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (owner is null) return;
        ApplyButton.IsEnabled = false;
        try
        {
            string? requested = string.IsNullOrWhiteSpace(WorkspaceBox.Text) ? null : WorkspaceBox.Text.Trim();
            await owner.ApplyWorkspaceAsync(requested);
        }
        catch (Exception exception) { owner.ShowError(exception); }
        finally { ApplyButton.IsEnabled = true; }
    }

    private async void BrowseWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (owner is null) return;
        FolderPicker picker = new();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null) WorkspaceBox.Text = folder.Path;
    }

    private async void RegenerateToken_Click(object sender, RoutedEventArgs e)
    {
        if (owner is null) return;
        ContentDialog confirmation = new()
        {
            XamlRoot = XamlRoot,
            Title = "接続トークンを再発行しますか？",
            Content = "現在の接続設定は使えなくなります。再発行後、CodexとClaude Codeの設定を更新してください。",
            PrimaryButtonText = "再発行",
            CloseButtonText = "キャンセル"
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            await owner.RegenerateTokenAsync();
            Refresh();
        }
        catch (Exception exception) { owner.ShowError(exception); }
    }

    private void CopyCodex_Click(object sender, RoutedEventArgs e) => Copy(CodexBox.Text);
    private void CopyClaude_Click(object sender, RoutedEventArgs e) => Copy(ClaudeBox.Text);
    private void CopyCodexAgentRequest_Click(object sender, RoutedEventArgs e)
    {
        if (owner?.AccessToken is string token) Copy(ConnectionSnippets.CodexAgentRequest(token));
    }

    private void CopyClaudeAgentRequest_Click(object sender, RoutedEventArgs e)
    {
        if (owner?.AccessToken is string token) Copy(ConnectionSnippets.ClaudeAgentRequest(token));
    }

    private static void Copy(string value)
    {
        DataPackage data = new();
        data.SetText(value);
        Clipboard.SetContent(data);
    }
}
