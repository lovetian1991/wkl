using System.Diagnostics;
using LanRemoteControl.Shared;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LanRemoteControl.Agent;

/// <summary>
/// Agent 服务宿主。以 BackgroundService 形式运行，编排所有被控端组件。
/// 启动流程：初始化 DesktopCapturer → 启动 CommunicationServer (port 19621) → 启动 DiscoveryResponder。
/// 会话活跃时：捕获帧 → 编码 → 发送帧数据；接收 InputCommand → 执行输入模拟。
/// </summary>
public class AgentService : BackgroundService
{
    private const int TcpPort = 19621;
    private const int FrameIntervalMs = 33; // ~30fps

    private readonly ILogger<AgentService> _logger;
    private readonly CrashRecoveryLogger _crashRecoveryLogger;

    private IDesktopCapturer? _capturer;
    private IFrameEncoder? _encoder;
    private ICommunicationServer? _commServer;
    private IDiscoveryResponder? _discoveryResponder;
    private ISessionManager? _sessionManager;
    private IInputSimulator? _inputSimulator;
    private AdaptiveFrameRateController? _frameRateController;
    private AdaptiveBandwidthController? _bandwidthController;

    public AgentService(ILogger<AgentService> logger, CrashRecoveryLogger crashRecoveryLogger)
    {
        _logger = logger;
        _crashRecoveryLogger = crashRecoveryLogger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Reset crash count on successful startup
        _crashRecoveryLogger.ResetCrashCount();

        try
        {
            // Initialize components
            _capturer = new DxgiDesktopCapturer();
            _encoder = new TurboJpegFrameEncoder();
            _sessionManager = new SessionManager();
            _inputSimulator = new Win32InputSimulator();
            _frameRateController = new AdaptiveFrameRateController();
            _bandwidthController = new AdaptiveBandwidthController();

            _commServer = new CommunicationServer(_sessionManager, _inputSimulator);
            _discoveryResponder = new DiscoveryResponder(TcpPort);

            // Start network services
            await _commServer.StartAsync(TcpPort, stoppingToken).ConfigureAwait(false);
            _ = Task.Run(() => _discoveryResponder.StartAsync(stoppingToken), stoppingToken);

            _logger.LogInformation("Agent service started on TCP port {Port}.", TcpPort);

            // Main capture-encode-send loop
            await RunCaptureLoopAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent service encountered a fatal error.");
            _crashRecoveryLogger.IncrementCrashCount();
            throw;
        }
        finally
        {
            await CleanupAsync().ConfigureAwait(false);
        }
    }

    private async Task RunCaptureLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var session = _sessionManager?.ActiveSession;

            if (session is null || !session.IsActive)
            {
                // No active session — wait briefly before checking again
                await Task.Delay(100, ct).ConfigureAwait(false);
                continue;
            }

            try
            {
                var frame = _capturer?.CaptureNextFrame(FrameIntervalMs);

                if (frame is null)
                {
                    await Task.Delay(FrameIntervalMs, ct).ConfigureAwait(false);
                    continue;
                }

                // Record frame for adaptive frame rate control
                _frameRateController?.RecordFrame();

                // Check if resolution adjustment is needed
                if (_frameRateController?.ShouldAdjustResolution(out float scaleFactor) == true)
                {
                    _capturer?.ReduceResolution(scaleFactor);
                }

                // Apply adaptive bandwidth quality
                if (_bandwidthController is not null && _encoder is not null)
                {
                    _encoder.Quality = _bandwidthController.RecommendedQuality;
                }

                // Encode frame
                var encoded = _encoder!.Encode(frame.Value);

                // Unmap staging texture after encoding (DxgiDesktopCapturer specific)
                if (_capturer is DxgiDesktopCapturer dxgiCapturer)
                {
                    dxgiCapturer.UnmapStagingTexture();
                }

                // Send frame to client
                var sw = Stopwatch.StartNew();
                await _commServer!.SendFrameAsync(session, encoded).ConfigureAwait(false);
                sw.Stop();

                // Record send metrics for bandwidth adaptation
                _bandwidthController?.RecordFrameSent(encoded.Length, sw.Elapsed);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error in capture loop iteration.");
                await Task.Delay(FrameIntervalMs, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task CleanupAsync()
    {
        _discoveryResponder?.Dispose();

        if (_commServer is not null)
        {
            try { await _commServer.StopAsync().ConfigureAwait(false); } catch { }
            _commServer.Dispose();
        }

        (_inputSimulator as IDisposable)?.Dispose();
        (_encoder as IDisposable)?.Dispose();
        _capturer?.Dispose();

        _logger.LogInformation("Agent service stopped.");
    }
}
