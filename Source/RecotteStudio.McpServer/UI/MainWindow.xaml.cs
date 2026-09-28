using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RecotteStudio.McpServer;

namespace RecotteStudio.McpServer.UI;

public sealed partial class MainWindow : Window
{
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private RecotteHttpServer? server;
    private string? token;
    private bool allowClose;
    private bool closing;

    internal string? WorkspaceRoot { get; private set; }
    internal string? AccessToken => token;
    internal Uri? ServerEndpoint => server?.Endpoint;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(840, 700));
        AppWindow.Closing += AppWindow_Closing;
        NavView.SelectedItem = HomeItem;
        if (ContentFrame.Content is null) ContentFrame.Navigate(typeof(HomePage), this);
    }

    public async Task StartAsync()
    {
        try
        {
            WorkspaceRoot = LocalSettings.LoadWorkspace();
            token = LocalSettings.LoadOrCreateToken();
            RefreshPage();
            await RestartServerAsync();
        }
        catch (Exception exception) { ShowError(exception); }
    }

    internal async Task ApplyWorkspaceAsync(string? requested)
    {
        if (requested is not null && !Directory.Exists(requested))
            throw new DirectoryNotFoundException("指定した作業フォルダが見つかりません。");
        WorkspaceRoot = requested is null ? null : Path.GetFullPath(requested);
        LocalSettings.SaveWorkspace(WorkspaceRoot);
        await RestartServerAsync();
    }

    internal async Task RegenerateTokenAsync()
    {
        token = LocalSettings.RegenerateToken();
        await RestartServerAsync();
        StatusBar.Message = "トークンを再発行しました。MCPクライアントの設定を更新してください。";
    }

    private async Task RestartServerAsync()
    {
        await lifecycle.WaitAsync();
        try
        {
            if (server is not null)
            {
                SetStatus(InfoBarSeverity.Informational, "停止中", "実行中の要求が終わるまで待っています。");
                await server.DisposeAsync();
                server = null;
                RefreshPage();
            }
            SetStatus(InfoBarSeverity.Informational, "起動中", "MCPサーバーを開始しています。");
            server = await RecotteHttpServer.StartAsync(WorkspaceRoot, token!);
            SetStatus(InfoBarSeverity.Success, "稼働中", "このウィンドウを閉じると接続を停止します。");
            RefreshPage();
        }
        finally { lifecycle.Release(); }
    }

    internal void ShowError(Exception exception) =>
        SetStatus(InfoBarSeverity.Error, "エラー", exception.Message);

    private void SetStatus(InfoBarSeverity severity, string title, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = title;
        StatusBar.Message = message;
    }

    internal void OpenSettings() => NavView.SelectedItem = NavView.SettingsItem;

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        Type page = args.IsSettingsSelected ? typeof(SettingsPage) : typeof(HomePage);
        if (ContentFrame.CurrentSourcePageType != page)
            ContentFrame.Navigate(page, this);
    }

    private void RefreshPage()
    {
        if (ContentFrame.Content is HomePage home) home.Refresh();
        if (ContentFrame.Content is SettingsPage settings) settings.Refresh();
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (allowClose) return;
        args.Cancel = true;
        if (closing) return;
        closing = true;
        await lifecycle.WaitAsync();
        try
        {
            if (server is not null)
            {
                SetStatus(InfoBarSeverity.Informational, "停止中", "実行中の要求が終わるまで待っています。");
                await server.DisposeAsync();
                server = null;
            }
        }
        finally
        {
            lifecycle.Release();
            allowClose = true;
            Close();
        }
    }
}
