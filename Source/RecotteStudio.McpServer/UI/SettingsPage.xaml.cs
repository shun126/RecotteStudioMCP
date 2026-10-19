using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace RecotteStudio.McpServer.UI;

public sealed partial class SettingsPage : Page
{
    private MainWindow? owner;

    public SettingsPage()
    {
        InitializeComponent();
        VersionText.Text = $"Recotte Studio MCP {AppVersion.Version}";
        BuildText.Text = AppVersion.BuildDescription;
        UserRulesPathText.Text = UserRulesProvider.DefaultPath;
    }

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
        FfmpegBox.Text = owner.Ffmpeg.ConfiguredPath ?? string.Empty;
        _ = ShowFfmpegStatusAsync(owner.Ffmpeg.ConfiguredPath);
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

    private async void ApplyFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        if (owner is null) return;
        ApplyFfmpegButton.IsEnabled = false;
        try
        {
            string? requested = RequestedFfmpegPath();
            await owner.ApplyFfmpegPathAsync(requested);
            FfmpegBox.Text = owner.Ffmpeg.ConfiguredPath ?? string.Empty;
            await ShowFfmpegStatusAsync(owner.Ffmpeg.ConfiguredPath);
        }
        catch (Exception exception) { FfmpegStatusText.Text = exception.Message; }
        finally { ApplyFfmpegButton.IsEnabled = true; }
    }

    private async void CheckFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        CheckFfmpegButton.IsEnabled = false;
        try { await ShowFfmpegStatusAsync(RequestedFfmpegPath()); }
        finally { CheckFfmpegButton.IsEnabled = true; }
    }

    private async void BrowseFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        if (owner is null) return;
        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add(".exe");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
        var file = await picker.PickSingleFileAsync();
        if (file is not null) FfmpegBox.Text = file.Path;
    }

    private string? RequestedFfmpegPath() => string.IsNullOrWhiteSpace(FfmpegBox.Text) ? null : FfmpegBox.Text.Trim().Trim('"');

    // Reports the ffmpeg that the given setting would use, without saving it.
    private async Task ShowFfmpegStatusAsync(string? configuredPath)
    {
        FfmpegStatusText.Text = "確認しています…";
        FfmpegResolution resolution = new FfmpegLocator(configuredPath).Resolve();
        if (!resolution.Found) { FfmpegStatusText.Text = resolution.Message; return; }
        FfmpegProbeResult probe = await FfmpegLocator.ProbeAsync(resolution.ExecutablePath!);
        string source = resolution.Source == FfmpegSource.Configured ? "指定したパス" : "PATH から検出";
        FfmpegStatusText.Text = probe.Success
            ? $"ffmpeg {probe.Version} を使用できます（{source}: {resolution.ExecutablePath}）"
            : $"{probe.Message}（{source}: {resolution.ExecutablePath}）";
    }

    // Selects UserRules.md in Explorer when it exists; otherwise opens the folder where the user creates it.
    private void OpenUserRulesFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(UserRulesProvider.DefaultDirectory);
            string arguments = File.Exists(UserRulesProvider.DefaultPath)
                ? $"/select,\"{UserRulesProvider.DefaultPath}\""
                : $"\"{UserRulesProvider.DefaultDirectory}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
        }
        catch (Exception exception) { owner?.ShowError(exception); }
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
