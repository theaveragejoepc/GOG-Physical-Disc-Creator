using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GogDisc.Core;
using GogDisc.Packager;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var resources = File.ReadAllText("src/GogDisc.Packager/App.xaml");
        app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(
            "<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" +
            resources.Split("<Application.Resources>")[1].Split("</Application.Resources>")[0] + "</ResourceDictionary>");
        var window = new MainWindow();
        var scratch = Path.Combine(Path.GetTempPath(), "gog-wizard-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            T Control<T>(string name) where T : FrameworkElement => (T)window.FindName(name);
            void Click(string name) => Control<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            void Set(string name, string value) => Control<TextBox>(name).Text = value;
            object? Call(string name) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            var tabs = Control<TabControl>("WizardTabs");
            var game1 = (TabItem)tabs.Items[1];
            var main = (TabItem)tabs.Items[0];
            var finish = Control<TabItem>("FinishTab");
            Assert(!Control<Button>("BackButton").IsEnabled, "Back disabled on first tab");
            Set("TitleBox", "My GOG Collection");
            if (args.Length > 0) Capture(window, args[0], "main-setup.png");
            Click("NextButton");
            Assert(tabs.SelectedItem == game1, "Next selects Game 1");
            string FileAt(string name) { var path = Path.Combine(scratch, name); File.WriteAllText(path, "synthetic fixture; never executed"); return path; }
            var base1 = FileAt("setup_first_game_1.0.exe");
            var patch1 = FileAt("patch_first_game_1.1.exe");
            var patch2 = FileAt("patch_first_game_1.2.exe");
            var dlc = FileAt("setup_first_game_dlc_1.0.exe");
            var base2 = FileAt("setup_second_game_1.0.exe");
            var cover1 = Path.Combine(scratch, "first-cover.png");
            var cover2 = Path.Combine(scratch, "second-cover.png");
            var pixel = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 32, 64, 128, 255 }, 4);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(pixel));
            using (var stream = File.Create(cover1)) png.Save(stream);
            File.Copy(cover1, cover2);
            var extras = Path.Combine(scratch, "extras"); Directory.CreateDirectory(extras);
            File.WriteAllText(Path.Combine(extras, "manual.txt"), "Fixture manual");
            Set("ExtrasBox", extras);
            Set("GameCoverBox", cover1);
            Set("SetupBox", base1);
            Set("GameTitleBox", "First Game");
            Set("PatchBox", patch1 + Environment.NewLine + patch2);
            Set("DlcBox", dlc);
            Set("AddonBox", patch1 + Environment.NewLine + dlc + Environment.NewLine + patch2);
            Click("AddGameButton");
            Assert(tabs.Items.Count == 4 && ((TabItem)tabs.SelectedItem).Header.ToString() == "Game 2", "Add Game creates and selects next tab");
            Assert(Control<TextBox>("SetupBox").Text == "", "New game starts empty");
            Set("SetupBox", base2);
            Set("GameTitleBox", "Second Game");
            Set("GameCoverBox", cover2);
            var game2 = (TabItem)tabs.SelectedItem;
            tabs.SelectedItem = game1;
            Assert(Control<TextBox>("SetupBox").Text == base1, "First game setup retained");
            Assert(Control<TextBox>("AddonBox").Text.Split(Environment.NewLine)[1] == dlc, "Custom interleaved order retained");
            if (args.Length > 0) Capture(window, args[0], "game-tab.png");
            tabs.SelectedItem = finish;
            var collection = (SetupCollection)Call("ScanGameTabs")!;
            Assert(collection.Games.Count == 2 && collection.Games[1].Family.SetupExecutable == base2, "Both tabs scanned");
            Assert(collection.Games[0].CoverImage == cover1 && collection.Games[1].CoverImage == cover2 && collection.Games[0].Family.Extras.Count == 1, "Per-game covers and extras retained");
            Assert(collection.Games[0].Family.Installers.Count == 4 && collection.Games[0].Family.Installers[2].EndsWith(Path.GetFileName(dlc)), "Patches and DLC order packaged");
            // Build fixture media to verify each tab feeds the existing manifest and copying pipeline.
            var result = CollectionBuilder.BuildAsync(new CollectionBuildRequest
            {
                Title = "Wizard fixture", Version = "1.0", Collection = collection,
                CapacityBytes = 50_000_000, ReserveBytes = 1_000_000, MediaName = "Test",
                OutputDirectory = Path.Combine(scratch, "output"), LauncherExecutable = FileAt("Launch.exe")
            }).GetAwaiter().GetResult();
            Assert(result.Manifest.CollectionGames.Count == 2 && result.Manifest.CollectionGames[0].Installers.Count == 4, "Collection manifest includes both games and add-ons");
            Assert(result.Manifest.CollectionGames.All(game => !string.IsNullOrWhiteSpace(game.CoverFile)) && result.Manifest.CollectionGames[0].CoverFile != result.Manifest.CollectionGames[1].CoverFile, "Each game cover copied independently");
            Assert(!Control<Button>("NextButton").IsEnabled, "Next disabled on Finish");
            if (args.Length > 0) { Capture(window, args[0], "finish-tab.png"); Capture(window, args[0], "finish-small.png", 860, 570); }
            Control<ComboBox>("DeploymentTypeBox").SelectedIndex = 0;
            Assert(game2.Visibility == Visibility.Collapsed && Control<TextBox>("SetupBox").Text == base1, "Standard mode uses first game");
            Control<ComboBox>("DeploymentTypeBox").SelectedIndex = 1;
            tabs.SelectedItem = game2;
            Assert(Control<TextBox>("SetupBox").Text == base2, "Mode switching preserves other game");
            Set("GameTitleBox", "First Game");
            try { Call("ScanGameTabs"); throw new Exception("Duplicate game title accepted"); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { }
            Set("GameTitleBox", "Second Game");
            tabs.SelectedItem = game1;
            Click("RemoveGameButton");
            Assert(tabs.Items.Count == 3 && Control<TextBox>("SetupBox").Text == base2, "Removing first game keeps the other game");
            Control<ComboBox>("DeploymentTypeBox").SelectedIndex = 2;
            Assert(Control<Expander>("KeyOptionsExpander").Visibility == Visibility.Visible && ((TabItem)tabs.Items[1]).Visibility == Visibility.Collapsed, "Key card mode retains settings and skips offline game tab");
            tabs.SelectedItem = main;
            Click("NextButton");
            Assert(tabs.SelectedItem == finish, "Key card navigation goes to Finish");
            var output = Control<TextBox>("OutputBox").Text;
            Click("ClearButton");
            Assert(tabs.Items.Count == 3 && Control<TextBox>("SetupBox").Text == "" && Control<TextBox>("OutputBox").Text == output, "Clear resets game inputs and preserves output");
            Console.WriteLine("PASS wizard navigation, isolated game inputs, installer order, fixture collection build, mode switching, duplicate names, removal, and reset.");
        }
        finally { window.Close(); Directory.Delete(scratch, true); app.Shutdown(); }
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }

    private static void Capture(Window window, string directory, string name, int width = 1120, int height = 740)
    {
        Directory.CreateDirectory(directory);
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.SystemIdle);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new System.Windows.Shapes.Rectangle { Width = width, Height = height, Fill = window.Background };
        background.Measure(new Size(width, height)); background.Arrange(new Rect(0, 0, width, height));
        bitmap.Render(background); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, name)); encoder.Save(output);
    }
}
