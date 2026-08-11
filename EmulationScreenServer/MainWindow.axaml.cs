using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EmulationScreenServer
{
    public partial class MainWindow : Window
    {
        private EmulationServerController _controller;
        private UiLogWriter _logWriter;
        private UpdateInfo? _pendingUpdate;
        private bool _updateCheckRunning;

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        private static extern uint SetThreadExecutionState(uint esFlags);
        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;

        public MainWindow()
        {
            InitializeComponent();
            _controller = new EmulationServerController();
            _controller.PublicRtspUrlResolved += OnPublicRtspUrlResolved;
            
            // Redirect console output to the Logs tab (no separate console window).
            _logWriter = new UiLogWriter(AppendLog);
            Console.SetOut(_logWriter);
            Console.SetError(_logWriter);

            // Prevent Windows from suspending the background process when minimized
            if (OperatingSystem.IsWindows())
            {
                SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);
            }

            this.Closing += (s, e) => {
                if (OperatingSystem.IsWindows())
                {
                    SetThreadExecutionState(ES_CONTINUOUS);
                }
                _controller.Stop();
            };

            AppVersionText.Text = $"Version {AppVersion.Current}";
            Opened += async (_, _) => await CheckForUpdatesAsync(showIfUpToDate: false);
        }

        private void AppendLog(string message)
        {
            if (LogTextBox.Text == null) LogTextBox.Text = "";
            LogTextBox.Text += message;
            if (LogTextBox.Text.Length > 10000)
            {
                LogTextBox.Text = LogTextBox.Text.Substring(LogTextBox.Text.Length - 10000);
            }
            LogTextBox.CaretIndex = LogTextBox.Text.Length;
        }

        private async void OnStartClicked(object sender, RoutedEventArgs e)
        {
            StartButton.IsEnabled = false;
            
            _controller.MonitorIndex = (int)(MonitorIndexSpinner.Value ?? 0);
            _controller.TargetFps = (int)(FpsSpinner.Value ?? 60);
            _controller.EnableAudio = EnableAudioCheckBox.IsChecked ?? true;
            _controller.EnableMouseForwarding = EnableMouseForwardingCheckBox.IsChecked ?? true;
            _controller.EnableControllerForwarding = EnableControllerForwardingCheckBox.IsChecked ?? true;

            await Task.Run(async () => {
                await _controller.StartAsync();
            });

            StatusText.Text = "Status: Running";
            StatusText.Foreground = Avalonia.Media.Brushes.LightGreen;
            LocalRtspUrlBox.Text = _controller.CurrentRtspUrl;
            PublicRtspUrlBox.Text = "Detecting public IP...";
            
            StopButton.IsEnabled = true;
            InputStatusText.Text = FormatInputStatus(
                EnableMouseForwardingCheckBox.IsChecked ?? true,
                EnableControllerForwardingCheckBox.IsChecked ?? true);
            InputStatusText.Foreground = Avalonia.Media.Brushes.LightGray;
            
            // Disable settings while running
            MonitorIndexSpinner.IsEnabled = false;
            FpsSpinner.IsEnabled = false;
            EnableAudioCheckBox.IsEnabled = false;
            EnableMouseForwardingCheckBox.IsEnabled = false;
            EnableControllerForwardingCheckBox.IsEnabled = false;
        }

        private void OnStopClicked(object sender, RoutedEventArgs e)
        {
            StopButton.IsEnabled = false;
            _ = StopServerAsync();
        }

        private async Task StopServerAsync()
        {
            if (!_controller.IsRunning) return;

            await Task.Run(() => _controller.Stop());
            await Dispatcher.UIThread.InvokeAsync(ApplyStoppedUi);
        }

        private void ApplyStoppedUi()
        {
            StatusText.Text = "Status: Stopped";
            StatusText.Foreground = Avalonia.Media.Brushes.DarkGray;
            LocalRtspUrlBox.Text = string.Empty;
            PublicRtspUrlBox.Text = string.Empty;
            InputStatusText.Text = "Not running";
            InputStatusText.Foreground = Avalonia.Media.Brushes.Gray;
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;

            MonitorIndexSpinner.IsEnabled = true;
            FpsSpinner.IsEnabled = true;
            EnableAudioCheckBox.IsEnabled = true;
            EnableMouseForwardingCheckBox.IsEnabled = true;
            EnableControllerForwardingCheckBox.IsEnabled = true;
        }

        private static string FormatInputStatus(bool mouse, bool controller)
        {
            if (mouse && controller) return "Mouse and controller forwarding active";
            if (mouse) return "Mouse forwarding active (controller disabled)";
            if (controller) return "Controller forwarding active (mouse disabled)";
            return "All input forwarding disabled";
        }

        private void OnPublicRtspUrlResolved(string url)
        {
            Dispatcher.UIThread.Post(() =>
            {
                PublicRtspUrlBox.Text = string.IsNullOrEmpty(url)
                    ? "Unable to detect public IP"
                    : url;
            });
        }

        private async void OnCopyLocalUrlClicked(object sender, RoutedEventArgs e)
        {
            await CopyToClipboardAsync(LocalRtspUrlBox.Text);
        }

        private async void OnCopyPublicUrlClicked(object sender, RoutedEventArgs e)
        {
            await CopyToClipboardAsync(PublicRtspUrlBox.Text);
        }

        private async Task CopyToClipboardAsync(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (text.StartsWith("Detecting", StringComparison.OrdinalIgnoreCase)) return;
            if (text.StartsWith("Unable", StringComparison.OrdinalIgnoreCase)) return;

            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(text);
            }
        }

        private async void OnCheckForUpdatesClicked(object sender, RoutedEventArgs e)
        {
            await CheckForUpdatesAsync(showIfUpToDate: true);
        }

        private async Task CheckForUpdatesAsync(bool showIfUpToDate)
        {
            if (_updateCheckRunning) return;
            _updateCheckRunning = true;

            try
            {
                var update = await Task.Run(() => GitHubUpdateService.CheckForUpdateAsync());
                Dispatcher.UIThread.Post(() =>
                {
                    if (update != null)
                        ShowUpdateBanner(update);
                    else if (showIfUpToDate)
                    {
                        UpdateBannerTitle.Text = "You're up to date";
                        UpdateBannerText.Text = $"Emulation Screen Server {AppVersion.Current} is the latest release.";
                        UpdateBanner.IsVisible = true;
                        InstallUpdateButton.IsVisible = false;
                        DismissUpdateButton.Content = "OK";
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Update] Check failed: {ex.Message}");
                if (showIfUpToDate)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        UpdateBannerTitle.Text = "Update check failed";
                        UpdateBannerText.Text = ex.Message;
                        UpdateBanner.IsVisible = true;
                        InstallUpdateButton.IsVisible = false;
                        DismissUpdateButton.Content = "OK";
                    });
                }
            }
            finally
            {
                _updateCheckRunning = false;
            }
        }

        private void ShowUpdateBanner(UpdateInfo update)
        {
            _pendingUpdate = update;
            UpdateBannerTitle.Text = $"Update available: {update.Version}";
            UpdateBannerText.Text = string.IsNullOrWhiteSpace(update.ReleaseNotes)
                ? "A newer version is available on GitHub."
                : update.ReleaseNotes.Trim();
            InstallUpdateButton.IsVisible = true;
            InstallUpdateButton.IsEnabled = true;
            InstallUpdateButton.Content = "Download & Install";
            DismissUpdateButton.Content = "Later";
            UpdateBanner.IsVisible = true;
        }

        private void OnDismissUpdateClicked(object? sender, RoutedEventArgs e)
        {
            UpdateBanner.IsVisible = false;
            InstallUpdateButton.IsVisible = true;
            DismissUpdateButton.Content = "Later";
        }

        private async void OnInstallUpdateClicked(object? sender, RoutedEventArgs e)
        {
            if (_pendingUpdate == null) return;

            InstallUpdateButton.IsEnabled = false;
            DismissUpdateButton.IsEnabled = false;
            InstallUpdateButton.Content = "Updating...";

            try
            {
                if (_controller.IsRunning)
                {
                    UpdateBannerText.Text = "Stopping server before update...";
                    await StopServerAsync();
                }

                var progress = new Progress<string>(message =>
                {
                    Dispatcher.UIThread.Post(() => UpdateBannerText.Text = message);
                });

                await Task.Run(() => GitHubUpdateService.ApplyUpdateAsync(_pendingUpdate, progress));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Update] Install failed: {ex.Message}");
                InstallUpdateButton.IsEnabled = true;
                DismissUpdateButton.IsEnabled = true;
                InstallUpdateButton.Content = "Download & Install";
                UpdateBannerText.Text = $"Update failed: {ex.Message}";
            }
        }
    }
}
