using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LanRemoteControl.Shared;

namespace LanRemoteControl.Controller;

/// <summary>
/// 主控端 WPF 主窗口。
/// 采用 code-behind 方式集成所有组件：设备发现、连接管理、帧渲染、输入采集。
/// </summary>
public partial class MainWindow : Window
{
    private static readonly Regex IPv4Regex = new(
        @"^((25[0-5]|2[0-4]\d|[01]?\d\d?)\.){3}(25[0-5]|2[0-4]\d|[01]?\d\d?)$",
        RegexOptions.Compiled);

    private const int DefaultPort = 19621;

    // Components
    private CommunicationClient _communicationClient;
    private readonly DiscoveryClient _discoveryClient;
    private readonly TurboJpegFrameDecoder _frameDecoder;
    private readonly InputCollector _inputCollector;

    // Rendering
    private WriteableBitmap? _bitmap;
    private int _remoteWidth;
    private int _remoteHeight;

    // FPS tracking
    private int _frameCount;
    private readonly Stopwatch _fpsStopwatch = new();

    // State
    private bool _isConnected;
    private bool _isControlEnabled; // 远程操控是否开启
    private CancellationTokenSource? _connectionCts;
    private bool _isValidIp;

    public MainWindow()
    {
        InitializeComponent();

        _communicationClient = new CommunicationClient();
        _discoveryClient = new DiscoveryClient();
        _frameDecoder = new TurboJpegFrameDecoder();
        _inputCollector = new InputCollector();

        _communicationClient.OnFrameReceived += OnFrameReceived;
        _communicationClient.OnDisconnected += OnDisconnected;
        _inputCollector.OnInputCaptured += OnInputCaptured;
    }

    /// <summary>Validates an IPv4 address string.</summary>
    public static bool IsValidIPv4(string input) => IPv4Regex.IsMatch(input);

    // ── IP Validation ──────────────────────────────────────────────

    private void IpTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var text = IpTextBox.Text.Trim();
        _isValidIp = IsValidIPv4(text);

        IpErrorText.Visibility = string.IsNullOrEmpty(text) || _isValidIp
            ? Visibility.Collapsed
            : Visibility.Visible;

        UpdateConnectButtonState();
    }

    // ── Device Discovery ───────────────────────────────────────────

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        ScanButton.IsEnabled = false;
        ScanButton.Content = "扫描中...";
        ConnectionStatusText.Text = "正在扫描设备...";

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var agents = await _discoveryClient.ScanAsync(TimeSpan.FromSeconds(3), cts.Token);

            DeviceListBox.ItemsSource = agents;
            ConnectionStatusText.Text = agents.Count > 0
                ? $"发现 {agents.Count} 台设备"
                : "未发现设备";
        }
        catch (Exception ex)
        {
            ConnectionStatusText.Text = $"扫描失败: {ex.Message}";
        }
        finally
        {
            ScanButton.IsEnabled = true;
            ScanButton.Content = "扫描设备";
        }
    }

    private void DeviceListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DeviceListBox.SelectedItem is DiscoveredAgent agent)
        {
            IpTextBox.Text = agent.IpAddress;
            _ = ConnectToAgentAsync(agent.IpAddress, agent.Port);
        }
    }

    private void DeviceListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (DeviceListBox.SelectedItem is DiscoveredAgent agent)
        {
            IpTextBox.Text = agent.IpAddress;
        }
    }

    // ── Connect / Disconnect ───────────────────────────────────────

    private void ControlToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isConnected) return;

        _isControlEnabled = !_isControlEnabled;

        if (_isControlEnabled)
        {
            ControlToggleButton.Content = "关闭操控";
            // 输入采集器在 RenderFrame 中 bitmap 创建时已 attach，这里只需切换标志
        }
        else
        {
            ControlToggleButton.Content = "开启操控";
        }
    }

    private void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        var ip = IpTextBox.Text.Trim();
        if (!IsValidIPv4(ip)) return;

        _ = ConnectToAgentAsync(ip, DefaultPort);
    }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        _ = DisconnectAsync();
    }

    private async Task ConnectToAgentAsync(string host, int port)
    {
        if (_isConnected) return;

        SetConnectingState();

        _connectionCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            await _communicationClient.ConnectAsync(host, port, _connectionCts.Token);
            SetConnectedState(host);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("rejected", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("Session rejected", StringComparison.OrdinalIgnoreCase))
        {
            // Session rejected — agent has an active connection
            ConnectionStatusText.Text = "被控端已有活跃连接";
            SetDisconnectedState();
        }
        catch (Exception ex)
        {
            ConnectionStatusText.Text = $"连接失败: {ex.Message}";
            SetDisconnectedState();
        }
    }

    private async Task DisconnectAsync()
    {
        if (!_isConnected) return;

        try
        {
            _inputCollector.Detach();
            await _communicationClient.DisconnectAsync();
        }
        catch
        {
            // Best-effort disconnect
        }
        finally
        {
            SetDisconnectedState();
        }
    }

    // ── Frame Rendering ────────────────────────────────────────────

    private void OnFrameReceived(EncodedFrame encoded)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () => RenderFrame(encoded));
    }

    private void RenderFrame(EncodedFrame encoded)
    {
        DecodedFrame decoded;
        try
        {
            decoded = _frameDecoder.Decode(encoded);
        }
        catch
        {
            return; // Skip bad frames
        }

        // Create or recreate bitmap if dimensions changed
        if (_bitmap is null || _bitmap.PixelWidth != decoded.Width || _bitmap.PixelHeight != decoded.Height)
        {
            _remoteWidth = decoded.Width;
            _remoteHeight = decoded.Height;
            _bitmap = new WriteableBitmap(decoded.Width, decoded.Height, 96, 96, PixelFormats.Bgra32, null);
            DesktopImage.Source = _bitmap;

            // Re-attach input collector with new dimensions
            _inputCollector.Detach();
            _inputCollector.Attach(DesktopImage, _remoteWidth, _remoteHeight);
        }

        // Update WriteableBitmap pixels
        _bitmap.Lock();
        try
        {
            var rect = new Int32Rect(0, 0, decoded.Width, decoded.Height);
            _bitmap.WritePixels(rect, decoded.PixelData, decoded.Stride, 0);
        }
        finally
        {
            _bitmap.Unlock();
        }

        // FPS counter
        _frameCount++;
        if (!_fpsStopwatch.IsRunning)
        {
            _fpsStopwatch.Start();
        }
        else if (_fpsStopwatch.ElapsedMilliseconds >= 1000)
        {
            double fps = _frameCount * 1000.0 / _fpsStopwatch.ElapsedMilliseconds;
            FpsText.Text = $"FPS: {fps:F1}";

            long latencyMs = (DateTime.UtcNow.Ticks - encoded.TimestampTicks) / TimeSpan.TicksPerMillisecond;
            if (latencyMs >= 0 && latencyMs < 10000)
            {
                LatencyText.Text = $"延迟: {latencyMs}ms";
            }

            _frameCount = 0;
            _fpsStopwatch.Restart();
        }
    }

    // ── Input Collector ────────────────────────────────────────────

    private async void OnInputCaptured(InputCommand command)
    {
        if (!_isConnected || !_isControlEnabled) return;

        try
        {
            await _communicationClient.SendInputCommandAsync(command);
        }
        catch
        {
            // Ignore send failures for individual input commands
        }
    }

    // ── Connection Disconnected (from server) ──────────────────────

    private void OnDisconnected()
    {
        Dispatcher.BeginInvoke(() =>
        {
            _inputCollector.Detach();
            ConnectionStatusText.Text = "连接已断开";
            SetDisconnectedState();
        });
    }

    // ── Window Close ───────────────────────────────────────────────

    private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _inputCollector.Detach();

        if (_isConnected)
        {
            try
            {
                await _communicationClient.DisconnectAsync();
            }
            catch
            {
                // Best-effort
            }
        }

        _communicationClient.OnFrameReceived -= OnFrameReceived;
        _communicationClient.OnDisconnected -= OnDisconnected;
        _inputCollector.OnInputCaptured -= OnInputCaptured;

        _frameDecoder.Dispose();
        _communicationClient.Dispose();
        _connectionCts?.Dispose();
    }

    // ── UI State Helpers ───────────────────────────────────────────

    private void SetConnectingState()
    {
        ConnectButton.IsEnabled = false;
        DisconnectButton.IsEnabled = false;
        ConnectionStatusText.Text = "正在连接...";
    }

    private void SetConnectedState(string host)
    {
        _isConnected = true;
        _isControlEnabled = false;
        ConnectButton.IsEnabled = false;
        DisconnectButton.IsEnabled = true;
        ControlToggleButton.IsEnabled = true;
        ControlToggleButton.Content = "开启操控";
        ConnectionStatusText.Text = $"已连接: {host}";
        _frameCount = 0;
        _fpsStopwatch.Restart();
    }

    private void SetDisconnectedState()
    {
        _isConnected = false;
        _bitmap = null;
        DesktopImage.Source = null;
        ConnectButton.IsEnabled = _isValidIp;
        DisconnectButton.IsEnabled = false;
        ControlToggleButton.IsEnabled = false;
        ControlToggleButton.Content = "开启操控";
        _isControlEnabled = false;
        FpsText.Text = "FPS: --";
        LatencyText.Text = "延迟: --";
        _fpsStopwatch.Stop();
        _frameCount = 0;

        // 重建 CommunicationClient 以支持重连
        _communicationClient.OnFrameReceived -= OnFrameReceived;
        _communicationClient.OnDisconnected -= OnDisconnected;
        _communicationClient.Dispose();
        _communicationClient = new CommunicationClient();
        _communicationClient.OnFrameReceived += OnFrameReceived;
        _communicationClient.OnDisconnected += OnDisconnected;

        if (string.IsNullOrEmpty(ConnectionStatusText.Text) ||
            ConnectionStatusText.Text == "正在连接...")
        {
            ConnectionStatusText.Text = "未连接";
        }
    }

    private void UpdateConnectButtonState()
    {
        ConnectButton.IsEnabled = _isValidIp && !_isConnected;
    }
}
