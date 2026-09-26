using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace GogDisc.Launcher;

// Native XInput, scoped to the active launcher window; no global keystrokes reach games or installers.
internal sealed class ControllerNavigation
{
    private readonly Window _window;
    private readonly Action _back;
    private readonly ControllerButtons[] _inputs = Enumerable.Range(0, 4).Select(_ => new ControllerButtons()).ToArray();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };

    public static void Attach(Window window, Action back) => _ = new ControllerNavigation(window, back);

    private ControllerNavigation(Window window, Action back)
    {
        _window = window;
        _back = back;
        _timer.Tick += Poll;
        window.Loaded += (_, _) => _timer.Start();
        window.Closed += (_, _) => { _timer.Stop(); _timer.Tick -= Poll; };
    }

    private void Poll(object? sender, EventArgs e)
    {
        for (uint index = 0; index < 4; index++)
        {
            uint result;
            State state;
            try { result = XInputGetState(index, out state); }
            catch (DllNotFoundException) { _timer.Stop(); return; }
            catch (EntryPointNotFoundException) { _timer.Stop(); return; }
            var action = _inputs[index].Read(state.Buttons, state.LeftX, state.LeftY,
                result == 0 && _window.IsActive && _window.IsEnabled, Environment.TickCount64);
            if (action != 0) Execute(action);
        }
    }

    internal void Execute(int action)
    {
        if (action == 0x2000) { _back(); return; }
        var focused = Keyboard.FocusedElement as UIElement;
        if (focused is null || !focused.IsVisible || !focused.IsEnabled || ReferenceEquals(focused, _window))
        {
            var buttons = Descendants(_window).OfType<Button>().Where(button => button.IsVisible && button.IsEnabled).ToList();
            var first = buttons.FirstOrDefault(button => button.Content is string label &&
                (label == "Play" || label.StartsWith("Install") || label == "Retry")) ?? buttons.FirstOrDefault();
            first?.Focus();
            first?.BringIntoView();
            return;
        }
        if (action == 0x1000)
        {
            var peer = UIElementAutomationPeer.CreatePeerForElement(focused);
            if (peer?.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle) toggle.Toggle();
            else if (peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke) invoke.Invoke();
            return;
        }
        var direction = action switch { 1 => FocusNavigationDirection.Up, 2 => FocusNavigationDirection.Down,
            4 => FocusNavigationDirection.Left, _ => FocusNavigationDirection.Right };
        focused.MoveFocus(new TraversalRequest(direction));
        (Keyboard.FocusedElement as FrameworkElement)?.BringIntoView();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct State
    {
        public uint Packet;
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short LeftX, LeftY, RightX, RightY;
    }

    [DllImport("xinput9_1_0.dll", ExactSpelling = true)]
    private static extern uint XInputGetState(uint userIndex, out State state);
}

// Edge-trigger A/B; repeat directions only. Re-arm on activation/connection so held buttons cannot click on return.
internal sealed class ControllerButtons
{
    private int _previous;
    private bool _armed;
    private long _repeatAt;

    public int Read(ushort buttons, short x, short y, bool active, long now)
    {
        var direction = (buttons & 1) != 0 || y > 16000 ? 1 :
            (buttons & 2) != 0 || y < -16000 ? 2 :
            (buttons & 4) != 0 || x < -16000 ? 4 :
            (buttons & 8) != 0 || x > 16000 ? 8 : 0;
        var pressed = (buttons & 0x3000) | direction;
        if (!active) { _armed = false; _previous = pressed; return 0; }
        if (!_armed) { _armed = pressed == 0; _previous = pressed; return 0; }
        var edges = pressed & ~_previous;
        var changed = (pressed & 15) != (_previous & 15);
        _previous = pressed;
        if ((edges & 0x2000) != 0) return 0x2000;
        if ((edges & 0x1000) != 0) return 0x1000;
        if (direction == 0) return 0;
        if (changed) { _repeatAt = now + 400; return direction; }
        if (now < _repeatAt) return 0;
        _repeatAt = now + 140;
        return direction;
    }
}
