using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using GogDisc.Core;
using Microsoft.Win32;

namespace GogDisc.Packager;

public partial class ArtworkWindow : Window
{
    private readonly ArtworkKind _kind;
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SteamGridDbClient _client;
    private bool _busy;
    private bool _closed;
    private int _page;
    private bool _hasPage;
    public string? SelectedPath { get; private set; }

    public ArtworkWindow(string title, ArtworkKind kind)
    {
        _kind = kind;
        _client = new SteamGridDbClient(_http);
        InitializeComponent();
        Title = Heading.Text = $"Choose {kind.ToString().ToLowerInvariant()} artwork";
        SearchBox.Text = title;
        try
        {
            ApiKeyBox.Password = ArtworkKeyStore.Load(ArtworkKeyStore.DefaultPath);
            if (ApiKeyBox.Password.Length > 0) Status.Text = "Saved API key loaded. Search for a game, or choose a local file.";
        }
        catch { Status.Text = "The saved key could not be opened. Enter and save it again, or choose Forget key."; }
        Closed += (_, _) => { _closed = true; _lifetime.Cancel(); _http.Dispose(); };
    }

    private void GetKey_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://www.steamgriddb.com/profile/preferences/api") { UseShellExecute = true });

    private void SaveKey_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ArtworkKeyStore.Save(ArtworkKeyStore.DefaultPath, ApiKeyBox.Password);
            Status.Text = "Key saved for your Windows account. It will load automatically next time.";
        }
        catch { Status.Text = "Could not save the key. Check that you entered a key and your user data folder is writable."; }
    }

    private void ForgetKey_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ArtworkKeyStore.Forget(ArtworkKeyStore.DefaultPath);
            ApiKeyBox.Clear();
            Status.Text = "Saved key removed from this computer. This does not revoke it on SteamGridDB.";
        }
        catch { Status.Text = "Could not remove the saved key. Check access to your user data folder."; }
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        await RunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(SearchBox.Text)) throw new InvalidOperationException("Enter a game title.");
            ArtworkList.ItemsSource = null;
            _hasPage = false;
            _page = 0;
            var key = ApiKeyBox.Password.Trim();
            GameBox.ItemsSource = await _client.SearchAsync(SearchBox.Text, key, _lifetime.Token);
            ArtworkKeyStore.Save(ArtworkKeyStore.DefaultPath, key);
            Status.Text = GameBox.Items.Count == 0 ? "No matching games. Try a different title or choose a local file." : "Choose the matching game above to see artwork.";
        });
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; Search_Click(sender, e); }
    }

    private async void Game_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_busy || GameBox.SelectedItem is not ArtworkGame) return;
        _page = 0;
        await LoadPageAsync();
    }

    private async Task LoadPageAsync() => await RunAsync(async () =>
    {
        if (GameBox.SelectedItem is not ArtworkGame game) return;
        _hasPage = false;
        ArtworkList.ItemsSource = null;
        var key = ApiKeyBox.Password.Trim();
        var options = await _client.ArtworkAsync(game.Id, _kind, _page, key, _lifetime.Token);
        ArtworkKeyStore.Save(ArtworkKeyStore.DefaultPath, key);
        _hasPage = options.Count > 0;
        var previews = new List<ArtworkPreview>();
        var failed = 0;
        foreach (var option in options)
        {
            _lifetime.Token.ThrowIfCancellationRequested();
            Status.Text = $"Loading previews: {previews.Count + failed + 1} of {options.Count}…";
            try
            {
                var bytes = await _client.DownloadAsync(option.Thumb, _lifetime.Token);
                previews.Add(new(option, Decode(bytes)));
            }
            catch (Exception) when (!_lifetime.IsCancellationRequested) { failed++; }
        }
        ArtworkList.ItemsSource = previews;
        Status.Text = $"Page {_page + 1}: {previews.Count} choices" + (failed > 0 ? $" ({failed} previews unavailable)." : ".") +
            (options.Count == 0 ? " Try Previous, another game, or a local file." : " Select artwork, then choose Use selected artwork.");
    });

    private async void Previous_Click(object sender, RoutedEventArgs e) { if (!_busy && _page > 0) { _page--; await LoadPageAsync(); } }
    private async void Next_Click(object sender, RoutedEventArgs e) { if (!_busy && _hasPage) { _page++; await LoadPageAsync(); } }
    private void Artwork_Changed(object sender, SelectionChangedEventArgs e) => UseButton.IsEnabled = !_busy && ArtworkList.SelectedItem is ArtworkPreview;

    private async void Use_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || ArtworkList.SelectedItem is not ArtworkPreview selected) return;
        await RunAsync(async () =>
        {
            var bytes = await _client.DownloadAsync(selected.Option.Url, _lifetime.Token);
            SelectedPath = SaveImage(Decode(bytes));
            DialogResult = true;
        });
    }

    private void Local_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = _kind == ArtworkKind.Icon ? "Artwork (*.png;*.jpg;*.jpeg;*.ico)|*.png;*.jpg;*.jpeg;*.ico" : "Artwork (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 24 * 1024 * 1024) throw new InvalidDataException("Choose an image smaller than 24 MiB.");
            SelectedPath = SaveImage(Decode(File.ReadAllBytes(dialog.FileName)));
            DialogResult = true;
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    private async Task RunAsync(Func<Task> action)
    {
        SetBusy(true);
        Status.Text = "Loading SteamGridDB…";
        try { await action(); }
        catch (OperationCanceledException) { if (!_closed) Status.Text = "The request timed out. Try again or choose a local file."; }
        catch (Exception ex) { if (!_closed) Status.Text = ex.Message; }
        finally { if (!_closed) SetBusy(false); }
    }

    private void SetBusy(bool value)
    {
        _busy = value;
        SearchButton.IsEnabled = GameBox.IsEnabled = ApiKeyBox.IsEnabled = !value;
        SaveKeyButton.IsEnabled = ForgetKeyButton.IsEnabled = !value;
        PreviousButton.IsEnabled = !value && _page > 0;
        NextButton.IsEnabled = !value && _hasPage;
        UseButton.IsEnabled = !value && ArtworkList.SelectedItem is ArtworkPreview;
    }

    private static BitmapSource Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        if ((long)frame.PixelWidth * frame.PixelHeight > 32_000_000) throw new InvalidDataException("Artwork must contain fewer than 32 million pixels.");
        frame.Freeze();
        return frame;
    }

    private static string SaveImage(BitmapSource image)
    {
        var folder = Path.Combine(AppPaths.Root, "Artwork");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = new FileStream(path, FileMode.CreateNew);
        encoder.Save(output);
        return path;
    }
}

public sealed record ArtworkPreview(ArtworkOption Option, BitmapSource Preview)
{
    public string Caption => Option.Caption;
}
