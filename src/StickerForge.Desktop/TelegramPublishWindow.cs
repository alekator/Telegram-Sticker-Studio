using System.Diagnostics;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input.Platform;
using StickerForge.Telegram;

namespace StickerForge.Desktop;

/// <summary>Experimental publishing UI; no project or media files are modified.</summary>
internal sealed class TelegramPublishWindow : Window
{
    private readonly string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StickerForge", "Telegram");
    private readonly TextBox apiId = new() { PlaceholderText = "API ID", IsEnabled = false }, apiHash = new() { PlaceholderText = "API hash", PasswordChar = '*', IsEnabled = false };
    private readonly TextBox vaultPassword = new() { PlaceholderText = "Local vault password (12+ characters)", PasswordChar = '*', IsEnabled = false };
    private readonly TextBox loginInput = new() { PlaceholderText = "Phone, verification code or 2FA password", PasswordChar = '*', IsEnabled = false };
    private readonly TextBox title = new() { PlaceholderText = "Pack title" }, shortName = new() { PlaceholderText = "Unique short name" };
    private readonly TextBlock message = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Text = "Telegram publishing is disabled: recovery and security checks are incomplete. Do not enter account credentials." };
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 1, Height = 12 };
    private readonly List<(string Path, TextBox Emoji)> stickers = [];
    private TelegramLogin? login;
    private CancellationTokenSource? operation;
    private bool busy, loginConfirmed;
    private string? link;
    private string? journalKey;

    public TelegramPublishWindow(string[] paths, string projectName)
    {
        Title = "Publish static sticker pack"; Width = 650; Height = 760; MinWidth = 480; MinHeight = 500;
        title.Text = projectName;
        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "1. Pack and emoji" }); panel.Children.Add(title); panel.Children.Add(shortName);
        foreach (var path in paths)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,150"), ColumnSpacing = 8 };
            var name = new TextBlock { Text = Path.GetFileName(path), TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var emoji = new TextBox { PlaceholderText = "Emoji" }; row.Children.Add(name); Grid.SetColumn(emoji, 1); row.Children.Add(emoji);
            panel.Children.Add(row); stickers.Add((path, emoji));
        }
        panel.Children.Add(new TextBlock { Text = "2. Telegram credentials and local vault" });
        panel.Children.Add(new TextBlock { Text = "Telegram sign-in and publication are disabled until recovery and security checks are complete.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        panel.Children.Add(apiId); panel.Children.Add(apiHash); panel.Children.Add(vaultPassword);
        panel.Children.Add(MakeButton("Open or create local vault", OpenVaultAsync));
        panel.Children.Add(loginInput); panel.Children.Add(MakeButton("Continue login", LoginAsync));
        panel.Children.Add(MakeButton("Log out", LogoutAsync));
        panel.Children.Add(new TextBlock { Text = "3. Publish" });
        panel.Children.Add(MakeButton("Check name and publish", PublishAsync));
        panel.Children.Add(MakeButton("Recover previous publication", RecoverAsync));
        panel.Children.Add(MakeButton("Cancel operation", () => { operation?.Cancel(); return Task.CompletedTask; }));
        panel.Children.Add(MakeButton("Copy link", CopyLinkAsync)); panel.Children.Add(MakeButton("Open link", OpenLinkAsync));
        panel.Children.Add(progress); panel.Children.Add(message);
        Content = new ScrollViewer { Content = panel };
        Closing += (_, e) => { if (busy) { e.Cancel = true; message.Text = "Cancel and wait for the active operation to finish."; } };
        Closed += (_, _) => { login?.Dispose(); login = null; };
    }

    private Button MakeButton(string label, Func<Task> action)
    {
        var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (label is "Open or create local vault" or "Continue login" or "Log out" or "Check name and publish" or "Recover previous publication") button.IsEnabled = false;
        button.Click += async (_, _) =>
        {
            if (label == "Cancel operation") { operation?.Cancel(); return; }
            if (busy) return;
            busy = true;
            try { await action(); } catch (OperationCanceledException) { message.Text = "Operation cancelled."; } catch (Exception ex) { message.Text = SafeError(ex); }
            finally { busy = false; }
        };
        return button;
    }
    private static string SafeError(Exception ex) => ex switch
    {
        PublicationUncertainException { InnerException: TL.RpcException rpc } when rpc.Message.Contains("FLOOD_WAIT", StringComparison.Ordinal) => $"Result uncertain. Wait {rpc.X} seconds, then use Recover; do not create the pack again.",
        PublicationUncertainException { InnerException: TL.RpcException rpc } when rpc.Message.Contains("FILE_REFERENCE", StringComparison.Ordinal) => "An uploaded file reference expired. Automatic recovery stopped; investigate the existing Telegram pack before taking further action.",
        PublicationUncertainException => "The result is uncertain. Use Recover; do not create the pack again.",
        CryptographicException => "The local vault password is incorrect or the vault is damaged.",
        TL.RpcException rpc when rpc.Message.Contains("FLOOD_WAIT", StringComparison.Ordinal) => $"Telegram rate limit reached. Wait {rpc.X} seconds before retrying.",
        TL.RpcException => "Telegram rejected the request. Check the account, code or pack details.",
        System.Net.Http.HttpRequestException => "Network error. Check the connection and retry only after checking the pack state.",
        FileNotFoundException => "A required local file is missing. Check the project sources.",
        ArgumentException => "Check the pack details and local settings.",
        InvalidDataException => "The image or local data could not be read.",
        InvalidOperationException => "The operation cannot continue in its current state.",
        _ => "The operation failed. Check your connection and input; no credentials are shown in this message."
    };
    private async Task OpenVaultAsync()
    {
        if (operation is not null || login is not null) throw new InvalidOperationException("Close the existing session first.");
        var password = vaultPassword.Text ?? ""; if (password.Length < 12) throw new ArgumentException("Use a local vault password of at least 12 characters.");
        Directory.CreateDirectory(dataDir); var path = Path.Combine(dataDir, "credentials.vault"); TelegramCredentials credentials;
        if (File.Exists(path)) credentials = await Task.Run(() => PasswordVault.LoadAsync(path, password));
        else
        {
            if (!int.TryParse(apiId.Text, out var id) || id <= 0 || string.IsNullOrWhiteSpace(apiHash.Text)) throw new ArgumentException("Enter a valid API ID and API hash.");
            credentials = new TelegramCredentials(id, apiHash.Text.Trim(), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
            await Task.Run(() => PasswordVault.SaveAsync(path, credentials, password));
        }
        login = new TelegramLogin(credentials.ApiId, credentials.ApiHash, Path.Combine(dataDir, "account.session"), credentials.SessionKey);
        journalKey = credentials.SessionKey;
        apiId.Text = ""; apiHash.Text = ""; vaultPassword.Text = ""; message.Text = "Vault opened. Enter your phone number and continue login; existing sessions can be resumed.";
    }
    private async Task LoginAsync()
    {
        if (login is null) throw new InvalidOperationException("Open the local vault first.");
        var input = loginInput.Text ?? ""; if (string.IsNullOrWhiteSpace(input)) throw new ArgumentException("Enter the requested login information.");
        loginInput.Text = ""; var next = await login.ContinueAsync(input);
        loginConfirmed = next is null;
        message.Text = next is null ? "Telegram login confirmed." : $"Telegram requests: {next}. Enter it and continue.";
    }
    private async Task LogoutAsync()
    {
        if (operation is not null) throw new InvalidOperationException("Wait for the current operation to finish.");
        if (login is null) return;
        var accountJournal = Path.Combine(dataDir, "publication-journal", login.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (Directory.Exists(accountJournal) && Directory.EnumerateFiles(accountJournal, "*.journal").Any())
            throw new InvalidOperationException("Resolve saved publication journals before removing their encryption key.");
        var current = login;
        await LogoutService.RevokeAndRemoveAsync(current.LogoutAsync, () => { current.Dispose(); login = null; journalKey = null; loginConfirmed = false; },
            Path.Combine(dataDir, "account.session"), Path.Combine(dataDir, "credentials.vault"));
        message.Text = "Logged out and local session and vault removed.";
    }
    private TelegramPublication Publisher() => new(new WTelegramStickerApi(login ?? throw new InvalidOperationException("Sign in first.")), new PublicationJournal(Path.Combine(dataDir, "publication-journal"), login!.UserId, journalKey ?? throw new InvalidOperationException("Vault is locked.")));
    private async Task PublishAsync()
    {
        if (login is null || !loginConfirmed || !login.IsAuthenticated) throw new InvalidOperationException("Sign in to Telegram first.");
        if (operation is not null) throw new InvalidOperationException("An operation is already running.");
        operation = new CancellationTokenSource(); var token = operation.Token;
        try
        {
            var prepared = new List<PreparedSticker>(); var preparer = new StaticStickerPreparer();
            foreach (var (path, emoji) in stickers)
            {
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(emoji.Text)) throw new ArgumentException($"Add emoji for {Path.GetFileName(path)}.");
                try { prepared.Add(new(await preparer.PrepareAsync(path, token), emoji.Text.Trim())); }
                catch (InvalidDataException ex) { throw new InvalidDataException($"{Path.GetFileName(path)}: {ex.Message}", ex); }
                progress.Value = (double)prepared.Count / Math.Max(1, stickers.Count); message.Text = $"Preparing {prepared.Count}/{stickers.Count}";
            }
            var updates = new Progress<PublishProgress>(p => { progress.Value = (double)p.Completed / p.Total; message.Text = $"{p.Stage}: {p.Completed}/{p.Total}"; });
            link = (await Publisher().PublishAsync(title.Text ?? "", shortName.Text ?? "", prepared, updates, token)).ToString(); message.Text = $"Published: {link}";
        }
        finally { operation.Dispose(); operation = null; }
    }
    private async Task RecoverAsync()
    {
        if (login is null || !loginConfirmed || !login.IsAuthenticated) throw new InvalidOperationException("Sign in to Telegram first.");
        link = (await Publisher().ReconcileAsync(shortName.Text ?? "")).ToString(); message.Text = $"Confirmed: {link}";
    }
    private async Task CopyLinkAsync()
    {
        if (link is null) throw new InvalidOperationException("No confirmed link is available.");
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard ?? throw new InvalidOperationException("Clipboard is unavailable."); await clipboard.SetTextAsync(link);
    }
    private Task OpenLinkAsync()
    {
        if (link is null) throw new InvalidOperationException("No confirmed link is available.");
        Process.Start(new ProcessStartInfo(link) { UseShellExecute = true }); return Task.CompletedTask;
    }
}


