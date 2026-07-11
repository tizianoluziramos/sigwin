using System;
using System.IO;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using signal_windows_client.Services;
using signal_windows_client.ViewModels;
using Windows.Storage;

namespace signal_windows_client
{
    /// <summary>
    /// Application entry point. Owns the singleton <see cref="SignalService"/> and
    /// the top-level view models, boots the core on launch, and drives log-out.
    /// </summary>
    public partial class App : Application
    {
        private static MainWindow? _window;
        private static string _dbPath = "";
        private static string _passphrase = "";
        private const string DeviceName = "Sigwin (Windows)";

        public static SignalService Signal { get; private set; } = null!;
        public static MainViewModel MainVm { get; private set; } = null!;
        public static LinkingViewModel LinkingVm { get; private set; } = null!;
        public static MainWindow MainWindowInstance => _window!;

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            var dispatcher = DispatcherQueue.GetForCurrentThread();
            Signal = new SignalService(dispatcher);
            MainVm = new MainViewModel();
            LinkingVm = new LinkingViewModel();

            Signal.ProvisioningUrlReceived += url => LinkingVm.ShowQr(url);
            Signal.EventReceived += MainVm.OnEvent;

            _dbPath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "sigwin.db");
            _passphrase = PassphraseStore.GetOrCreate();

            _window = new MainWindow();
            _window.Activate();

            StartCore();
        }

        private static void StartCore()
        {
            try
            {
                Signal.Start(_dbPath, _passphrase, DeviceName);
            }
            catch (Exception e)
            {
                LinkingVm.StatusText = "Failed to start core: " + e.Message;
            }
        }

        /// <summary>
        /// Log out: stop the core (releasing the store), delete the local store,
        /// reset the UI to the linking screen, and start fresh so a new QR is shown.
        /// </summary>
        public static void LogOut()
        {
            Signal.Stop();
            DeleteStore();
            MainVm.Reset();
            LinkingVm.Reset();
            _window?.ShowLinking();
            StartCore();
        }

        private static void DeleteStore()
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                var path = _dbPath + suffix;
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    try
                    {
                        if (File.Exists(path)) File.Delete(path);
                        break;
                    }
                    catch
                    {
                        Thread.Sleep(100); // store handle may take a moment to release
                    }
                }
            }
        }
    }
}
