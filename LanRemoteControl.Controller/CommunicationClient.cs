using System.IO;
using System.Net.Sockets;
using LanRemoteControl.Shared;

namespace LanRemoteControl.Controller;

/// <summary>
/// 基于原生 TCP Socket 的通信客户端实现。
/// 包含心跳保活、TCP KeepAlive、断线检测。
/// </summary>
public class CommunicationClient : ICommunicationClient
{
    private const int HeartbeatIntervalMs = 5000;  // 每5秒发一次心跳
    private const int HeartbeatTimeoutMs = 15000;  // 15秒没收到任何数据判定断连

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private Task? _heartbeatTask;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private volatile bool _connected;
    private bool _disposed;
    private long _lastReceivedTicks;

    public event Action<EncodedFrame>? OnFrameReceived;
    public event Action? OnDisconnected;

    public bool IsConnected => _connected;

    public async Task ConnectAsync(string host, int port, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _tcpClient = new TcpClient();
        _tcpClient.NoDelay = true;
        // TCP KeepAlive
        _tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        _tcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 10);
        _tcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 5);
        _tcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);

        await _tcpClient.ConnectAsync(host, port, ct).ConfigureAwait(false);
        _stream = _tcpClient.GetStream();

        var request = new SessionRequestPayload(Environment.MachineName, 1);
        var requestBytes = ProtocolSerializer.SerializeSessionRequest(request);
        await ProtocolSerializer.WriteMessageAsync(_stream, MessageType.SessionRequest, requestBytes, ct).ConfigureAwait(false);
        await _stream.FlushAsync(ct).ConfigureAwait(false);

        var (msgType, payload) = await ProtocolSerializer.ReadMessageAsync(_stream, ct).ConfigureAwait(false);

        if (msgType != MessageType.SessionResponse)
        {
            _tcpClient.Dispose(); _tcpClient = null; _stream = null;
            throw new InvalidOperationException($"Expected SessionResponse but received {msgType}.");
        }

        var response = ProtocolSerializer.DeserializeSessionResponse(payload);
        if (!response.Accepted)
        {
            _tcpClient.Dispose(); _tcpClient = null; _stream = null;
            throw new InvalidOperationException(response.RejectReason ?? "Session rejected by agent.");
        }

        _connected = true;
        _lastReceivedTicks = DateTime.UtcNow.Ticks;
        _receiveCts = new CancellationTokenSource();
        _receiveTask = ReceiveLoopAsync(_receiveCts.Token);
        _heartbeatTask = HeartbeatLoopAsync(_receiveCts.Token);
    }

    public async Task DisconnectAsync()
    {
        if (!_connected || _stream is null) return;

        try
        {
            await _sendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await ProtocolSerializer.WriteMessageAsync(_stream, MessageType.SessionClose, ReadOnlyMemory<byte>.Empty).ConfigureAwait(false);
                await _stream.FlushAsync().ConfigureAwait(false);
            }
            finally { _sendLock.Release(); }

            using var ackCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { await ProtocolSerializer.ReadMessageAsync(_stream, ackCts.Token).ConfigureAwait(false); } catch { }
        }
        catch { }
        finally { CleanupConnection(); }
    }

    public async Task SendInputCommandAsync(InputCommand command)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_connected || _stream is null) return; // 静默忽略而不是抛异常

        var buffer = new byte[ProtocolSerializer.InputCommandSize];
        ProtocolSerializer.WriteInputCommand(buffer, command);

        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await ProtocolSerializer.WriteMessageAsync(_stream, MessageType.InputCommand, buffer).ConfigureAwait(false);
            await _stream.FlushAsync().ConfigureAwait(false);
        }
        catch
        {
            // 发送失败，连接可能已断
        }
        finally { _sendLock.Release(); }
    }

    /// <summary>定期发送心跳，检测连接是否存活</summary>
    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        try
        {
            while (_connected && !ct.IsCancellationRequested)
            {
                await Task.Delay(HeartbeatIntervalMs, ct).ConfigureAwait(false);

                if (!_connected || _stream is null) break;

                // 检查是否超时没收到任何数据
                long elapsed = DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastReceivedTicks);
                if (elapsed > TimeSpan.FromMilliseconds(HeartbeatTimeoutMs).Ticks)
                {
                    // 超时，判定连接断开
                    break;
                }

                // 发送心跳
                try
                {
                    await _sendLock.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        await ProtocolSerializer.WriteMessageAsync(_stream, MessageType.Heartbeat, ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false);
                        await _stream.FlushAsync(ct).ConfigureAwait(false);
                    }
                    finally { _sendLock.Release(); }
                }
                catch
                {
                    break; // 发送心跳失败，连接已断
                }
            }
        }
        catch (OperationCanceledException) { }

        // 心跳超时或失败，触发断连
        if (_connected)
        {
            _connected = false;
            OnDisconnected?.Invoke();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (_connected && !ct.IsCancellationRequested)
            {
                var (msgType, payload) = await ProtocolSerializer.ReadMessageAsync(_stream!, ct).ConfigureAwait(false);

                // 收到任何数据都更新最后接收时间
                Interlocked.Exchange(ref _lastReceivedTicks, DateTime.UtcNow.Ticks);

                switch (msgType)
                {
                    case MessageType.FrameData:
                        HandleFrameData(payload);
                        break;
                    case MessageType.SessionCloseAck:
                        return;
                    case MessageType.Heartbeat:
                        break;
                    default:
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (EndOfStreamException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            if (_connected)
            {
                _connected = false;
                OnDisconnected?.Invoke();
            }
        }
    }

    private void HandleFrameData(byte[] payload)
    {
        if (payload.Length < ProtocolSerializer.FrameHeaderSize) return;
        var header = ProtocolSerializer.ReadFrameHeader(payload);
        if (header.CompressedLength <= 0 || ProtocolSerializer.FrameHeaderSize + header.CompressedLength > payload.Length) return;

        var jpegData = new byte[header.CompressedLength];
        Buffer.BlockCopy(payload, ProtocolSerializer.FrameHeaderSize, jpegData, 0, header.CompressedLength);

        OnFrameReceived?.Invoke(new EncodedFrame(jpegData, header.CompressedLength, header.Width, header.Height, header.TimestampTicks, header.SequenceNumber));
    }

    private void CleanupConnection()
    {
        _connected = false;
        if (_receiveCts is not null) { _receiveCts.Cancel(); _receiveCts.Dispose(); _receiveCts = null; }
        _stream = null;
        if (_tcpClient is not null) { _tcpClient.Dispose(); _tcpClient = null; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CleanupConnection();
        _sendLock.Dispose();
    }
}
