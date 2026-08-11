using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;

namespace EmulationScreenServer
{
    public partial class MainWindow : Window
    {
        private EmulationServerController _controller;
        private UiLogWriter _logWriter;

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
            
            Task.Run(() => {
                _controller.Stop();
                Dispatcher.UIThread.Post(() => {
                    StatusText.Text = "Status: Stopped";
                    StatusText.Foreground = Avalonia.Media.Brushes.DarkGray;
                    LocalRtspUrlBox.Text = string.Empty;
                    PublicRtspUrlBox.Text = string.Empty;
                    InputStatusText.Text = "Not running";
                    InputStatusText.Foreground = Avalonia.Media.Brushes.Gray;
                    StartButton.IsEnabled = true;
                    
                    // Re-enable settings
                    MonitorIndexSpinner.IsEnabled = true;
                    FpsSpinner.IsEnabled = true;
                    EnableAudioCheckBox.IsEnabled = true;
                    EnableMouseForwardingCheckBox.IsEnabled = true;
                    EnableControllerForwardingCheckBox.IsEnabled = true;
                });
            });
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
    }
}
