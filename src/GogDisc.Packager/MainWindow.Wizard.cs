using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GogDisc.Core;
using Microsoft.Win32;

namespace GogDisc.Packager;

public partial class MainWindow
{
    // The editor is shared by the game tabs; each tab retains its own inputs.
    private sealed class GameInputs
    {
        public string Setup = "", Extras = "", Patches = "", Dlcs = "", Order = "", Cover = "", Title = "", Version = "1.0";
        public bool PreservePatches;
        public int ProductType;
    }

    private TabItem? _editingGame;
    private bool _loadingGame;
    private bool _wizardReady;
    private bool _busy;

    private IEnumerable<TabItem> GameTabs => WizardTabs.Items.OfType<TabItem>().Where(tab => tab.Tag is GameInputs);

    private void InitializeWizard()
    {
        FirstGameTab.Tag = new GameInputs();
        _editingGame = FirstGameTab;
        _wizardReady = true;
        WizardTabs.SelectedItem = MainSetupTab;
        VersionBox.Text = "1.0";
        try { SavedArtworkKeyBox.Password = ArtworkKeyStore.Load(ArtworkKeyStore.DefaultPath); }
        catch { ArtworkKeyStatus.Text = "The saved key could not be read. Enter it again and save."; }
        UpdateWizardMode();
    }

    private void SaveGameInputs()
    {
        if (_editingGame?.Tag is not GameInputs game) return;
        game.Setup = SetupBox.Text;
        game.Extras = ExtrasBox.Text;
        game.Patches = PatchBox.Text;
        game.Dlcs = DlcBox.Text;
        game.Order = AddonBox.Text;
        game.Cover = GameCoverBox.Text;
        game.Title = GameTitleBox.Text;
        game.Version = GameVersionBox.Text;
        game.PreservePatches = IncludePatchesBox.IsChecked == true;
        game.ProductType = ProductTypeBox.SelectedIndex;
    }

    private void EditGame(TabItem tab, bool reload = false)
    {
        if (tab == _editingGame && !reload) return;
        if (!reload) SaveGameInputs();
        _loadingGame = true;
        try
        {
            if (_editingGame is not null) _editingGame.Content = null;
            _editingGame = tab;
            tab.Content = GameEditor;
            var game = (GameInputs)tab.Tag;
            SetupBox.Text = game.Setup;
            ExtrasBox.Text = game.Extras;
            PatchBox.Text = game.Patches;
            DlcBox.Text = game.Dlcs;
            AddonBox.Text = game.Order;
            GameCoverBox.Text = game.Cover;
            GameTitleBox.Text = game.Title;
            GameVersionBox.Text = game.Version;
            IncludePatchesBox.IsChecked = game.PreservePatches;
            ProductTypeBox.SelectedIndex = game.ProductType;
        }
        finally { _loadingGame = false; }
    }

    private void WizardTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_wizardReady || !ReferenceEquals(e.Source, WizardTabs)) return;
        if (WizardTabs.SelectedItem is TabItem { Tag: GameInputs } tab) EditGame(tab);
        UpdateNavigation();
    }

    private void UpdateWizardMode()
    {
        if (!_wizardReady) return;
        if (!IsCollection) EditGame(FirstGameTab);
        foreach (var tab in GameTabs)
            tab.Visibility = IsKeyMedia || (!IsCollection && tab != FirstGameTab) ? Visibility.Collapsed : Visibility.Visible;
        foreach (var control in new FrameworkElement[] { IndividualCoverPanel, GameIdentityPanel, RemoveGameButton, ImportCollectionExpander })
            control.Visibility = IsCollection ? Visibility.Visible : Visibility.Collapsed;
        ProductTypeBox.Visibility = ProductTypeLabel.Visibility = IsCollection ? Visibility.Collapsed : Visibility.Visible;
        KeyOptionsExpander.Visibility = IsKeyMedia ? Visibility.Visible : Visibility.Collapsed;
        if (WizardTabs.SelectedItem is TabItem selected && selected.Visibility != Visibility.Visible)
            WizardTabs.SelectedItem = MainSetupTab;
        UpdateNavigation();
    }

    private void UpdateNavigation()
    {
        var tabs = WizardTabs.Items.OfType<TabItem>().Where(tab => tab.Visibility == Visibility.Visible).ToList();
        var index = tabs.IndexOf((TabItem)WizardTabs.SelectedItem);
        BackButton.IsEnabled = !_busy && index > 0;
        NextButton.IsEnabled = !_busy && index >= 0 && index < tabs.Count - 1;
        RemoveGameButton.IsEnabled = GameTabs.Count() > 1;
    }

    private void Navigate(int direction)
    {
        var tabs = WizardTabs.Items.OfType<TabItem>().Where(tab => tab.Visibility == Visibility.Visible).ToList();
        var index = tabs.IndexOf((TabItem)WizardTabs.SelectedItem) + direction;
        if (index >= 0 && index < tabs.Count) WizardTabs.SelectedItem = tabs[index];
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Navigate(-1);
    private void Next_Click(object sender, RoutedEventArgs e) => Navigate(1);

    private void AddGame_Click(object sender, RoutedEventArgs e)
    {
        SaveGameInputs();
        DeploymentTypeBox.SelectedIndex = 1;
        var tab = new TabItem { Header = $"Game {GameTabs.Count() + 1}", Tag = new GameInputs() };
        WizardTabs.Items.Insert(WizardTabs.Items.IndexOf(FinishTab), tab);
        WizardTabs.SelectedItem = tab;
        ResetScan();
    }

    private void RemoveGame_Click(object sender, RoutedEventArgs e)
    {
        if (GameTabs.Count() < 2 || _editingGame is null) return;
        var removed = _editingGame;
        var replacement = GameTabs.First(tab => tab != removed);
        EditGame(replacement);
        WizardTabs.Items.Remove(removed);
        FirstGameTab = GameTabs.First();
        var number = 0;
        foreach (var tab in GameTabs) tab.Header = $"Game {++number}";
        WizardTabs.SelectedItem = replacement;
        UpdateNavigation();
        ResetScan();
    }

    private static string[] Lines(string text) => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Select(path => path.Trim()).Where(path => path.Length > 0).ToArray();

    private void InstallerLists_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_wizardReady || _loadingGame) return;
        var selected = Lines(PatchBox.Text).Concat(Lines(DlcBox.Text)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var retained = Lines(AddonBox.Text).Where(path => selected.Contains(path, StringComparer.OrdinalIgnoreCase));
        AddonBox.Text = string.Join(Environment.NewLine, retained.Concat(selected).Distinct(StringComparer.OrdinalIgnoreCase));
        ResetScan();
    }

    private void BrowseInstallers(TextBox box)
    {
        var dialog = new OpenFileDialog { Filter = "GOG installers (*.exe)|*.exe", Multiselect = true };
        if (dialog.ShowDialog(this) == true)
            box.Text = string.Join(Environment.NewLine, Lines(box.Text).Concat(dialog.FileNames).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private void BrowsePatches_Click(object sender, RoutedEventArgs e) => BrowseInstallers(PatchBox);
    private void BrowseDlcs_Click(object sender, RoutedEventArgs e) => BrowseInstallers(DlcBox);

    private void BrowseGameCover_Click(object sender, RoutedEventArgs e)
    {
        var title = string.IsNullOrWhiteSpace(GameTitleBox.Text) ? SetupNameParser.InferTitle(SetupBox.Text) : GameTitleBox.Text;
        var dialog = new ArtworkWindow(title, ArtworkKind.Cover) { Owner = this };
        if (dialog.ShowDialog() == true) { GameCoverBox.Text = dialog.SelectedPath; ResetScan(); }
    }

    private SetupCollection ScanGameTabs()
    {
        SaveGameInputs();
        var games = GameTabs.Select(tab =>
        {
            var input = (GameInputs)tab.Tag;
            var family = InstallerQueue.Combine(SetupFamilyScanner.Scan(input.Setup, EmptyToNull(input.Extras), input.PreservePatches), Lines(input.Order));
            var title = EmptyToNull(input.Title) ?? CultureInfo.CurrentCulture.TextInfo.ToTitleCase(SetupNameParser.InferTitle(input.Setup));
            if (string.IsNullOrWhiteSpace(title)) throw new InvalidDataException($"Enter a game title for {tab.Header}.");
            if (!string.IsNullOrWhiteSpace(input.Cover) && !File.Exists(input.Cover)) throw new FileNotFoundException($"Cover artwork not found for {title}.", input.Cover);
            return new SetupCollectionGame(title, EmptyToNull(input.Version) ?? "1.0", family) { CoverImage = EmptyToNull(input.Cover) };
        }).ToList();
        if (games.Count < 2) throw new InvalidDataException("A collection needs at least two games. Use Add Game on the game tab.");
        if (games.GroupBy(game => PackageBuilder.SanitizeFileName(game.Title), StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new InvalidDataException("Two games have the same disc folder name. Give each game a unique title under Installation order and game details.");
        return new SetupCollection { RootDirectory = "", Games = games };
    }

    private void ImportCollection_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Choose a collection folder containing one folder per game" };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            var collection = SetupCollectionScanner.Scan(picker.FolderName);
            SaveGameInputs();
            // Import adds tabs; existing manual selections are retained.
            foreach (var game in collection.Games)
            {
                var addons = game.Family.Installers.Skip(1).Select(path => game.Family.InstallerFiles.Single(file => file.RelativePath == path).FullPath).ToArray();
                var inputs = new GameInputs
                {
                    Setup = game.Family.SetupExecutable, Title = game.Title, Version = game.Version,
                    Extras = Directory.Exists(Path.Combine(picker.FolderName, game.Title, "Extras")) ? Path.Combine(picker.FolderName, game.Title, "Extras") : "",
                    Cover = game.CoverImage ?? "", Order = string.Join(Environment.NewLine, addons),
                    Patches = string.Join(Environment.NewLine, addons.Where(path => Path.GetFileName(path).StartsWith("patch_", StringComparison.OrdinalIgnoreCase))),
                    Dlcs = string.Join(Environment.NewLine, addons.Where(path => !Path.GetFileName(path).StartsWith("patch_", StringComparison.OrdinalIgnoreCase)))
                };
                var empty = GameTabs.FirstOrDefault(tab => tab.Tag is GameInputs { Setup: "", Extras: "", Patches: "", Dlcs: "", Cover: "", Title: "" });
                if (empty is not null)
                {
                    empty.Tag = inputs;
                    if (empty == _editingGame) EditGame(empty, reload: true);
                }
                else
                {
                    var tab = new TabItem { Header = $"Game {GameTabs.Count() + 1}", Tag = inputs };
                    WizardTabs.Items.Insert(WizardTabs.Items.IndexOf(FinishTab), tab);
                }
            }
            if (string.IsNullOrWhiteSpace(TitleBox.Text)) TitleBox.Text = Path.GetFileName(picker.FolderName);
            ResetScan();
            StatusText.Text = $"Imported {collection.Games.Count} games.";
            UpdateNavigation();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void SaveArtworkKey_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ArtworkKeyStore.Save(ArtworkKeyStore.DefaultPath, SavedArtworkKeyBox.Password);
            ArtworkKeyStatus.Text = "Key saved securely for your Windows account.";
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void ForgetArtworkKey_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ArtworkKeyStore.Forget(ArtworkKeyStore.DefaultPath);
            SavedArtworkKeyBox.Clear();
            ArtworkKeyStatus.Text = "Saved key removed from this computer.";
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        ClearBackupInputs();
        LogBox.Clear();
        ActivityPanel.Visibility = Visibility.Collapsed;
        WizardTabs.SelectedItem = MainSetupTab;
    }

    private void ClearGameTabs()
    {
        EditGame(FirstGameTab);
        foreach (var tab in GameTabs.Where(tab => tab != FirstGameTab).ToArray()) WizardTabs.Items.Remove(tab);
        PatchBox.Clear();
        DlcBox.Clear();
        GameCoverBox.Clear();
        GameTitleBox.Clear();
        GameVersionBox.Text = "1.0";
        SaveGameInputs();
        UpdateNavigation();
    }
}
