using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GogDisc.Launcher;

public enum InstallerStepChoice { Cancel, Install, Skip }

public sealed class InstallerStepWindow : Window
{
    public InstallerStepChoice Choice { get; private set; }

    public InstallerStepWindow(string fileName, bool canSkip, int? exitCode = null)
    {
        Title = exitCode is null ? "Install add-on" : "Installer did not finish";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(17, 21, 29));
        Foreground = Brushes.White;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = fileName, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var message = exitCode is null ? "Install this patch or DLC next?" : $"The installer returned code {exitCode}. Earlier steps may have completed.";
        if (canSkip) message += "\n\nIf this add-on is already installed or unneeded, choose Skip. Later patches may depend on it; skip only when appropriate.";
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 20) });
        var buttons = new System.Windows.Controls.Primitives.UniformGrid { Columns = canSkip ? 3 : 2 };
        Add(exitCode is null ? "Install" : "Retry", InstallerStepChoice.Install, "PrimaryButton");
        if (canSkip) Add("Skip", InstallerStepChoice.Skip, "SecondaryButton");
        Add("Cancel", InstallerStepChoice.Cancel, "SecondaryButton");
        panel.Children.Add(buttons);
        Content = panel;
        ControllerNavigation.Attach(this, () => { Choice = InstallerStepChoice.Cancel; Close(); });

        void Add(string label, InstallerStepChoice choice, string style)
        {
            var button = new Button { Content = label, Margin = new Thickness(4), Style = (Style)FindResource(style), IsCancel = choice == InstallerStepChoice.Cancel };
            button.Click += (_, _) => { Choice = choice; DialogResult = choice != InstallerStepChoice.Cancel; };
            buttons.Children.Add(button);
        }
    }
}
