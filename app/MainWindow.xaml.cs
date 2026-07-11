using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using signal_windows_client.Interop;
using signal_windows_client.Views;
using Windows.UI;

namespace signal_windows_client
{
    /// <summary>
    /// Root window. Shows the linking screen first, then switches to the chat
    /// shell as soon as the core starts delivering events (i.e. once linked).
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private bool _wentToShell;

        public MainWindow()
        {
            InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            ApplyTitleBarColors();
            ShowLinking();
        }

        private void ApplyTitleBarColors()
        {
            if (!AppWindowTitleBar.IsCustomizationSupported()) return;
            var titleBar = AppWindow.TitleBar;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = Colors.White;
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(40, 255, 255, 255);
            titleBar.ButtonHoverForegroundColor = Colors.White;
            titleBar.ButtonPressedBackgroundColor = Color.FromArgb(60, 255, 255, 255);
            titleBar.ButtonPressedForegroundColor = Colors.White;
            titleBar.ButtonInactiveForegroundColor = Color.FromArgb(160, 255, 255, 255);
        }

        /// <summary>Show the linking screen and re-arm the switch to the shell.</summary>
        public void ShowLinking()
        {
            _wentToShell = false;
            App.Signal.EventReceived -= OnFirstEvent;
            App.Signal.EventReceived += OnFirstEvent;
            RootFrame.Navigate(typeof(LinkingPage));
        }

        private void OnFirstEvent(SignalEvent ev)
        {
            if (_wentToShell) return;
            _wentToShell = true;
            App.Signal.EventReceived -= OnFirstEvent;
            RootFrame.Navigate(typeof(MainShellPage));
        }
    }
}
