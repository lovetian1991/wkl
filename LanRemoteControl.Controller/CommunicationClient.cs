using System.IO;
using System.Net.Sockets;
using LanRemoteControl.Shared;

namespace LanRemoteControl.Controller;

/// <summary>
/// 基于原生 TCP Socket 的通信客户端实现。
/// 负责与被控端建立会话、接收帧数据和发送输入指令。
/// </summary>
public class CommunicationClient : ICommunicationClient
{
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private volatile bool _connected;
    private bool _disposed;

    public event Action<EncodedFrame>? OnFrameReceived;
    public event Action? OnDisconnected;

    public bool IsConnected => _connected;

    public async Task ConnectAsync(string host, int port, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(host, port, ct).ConfigureAwait(false);
        _stream = _tcpClient.GetStream();

        // Send SessionRequest
        var request = new SessionRequestPayload(
            ControllerName: Environment.MachineName,
            ProtocolVersion: 1
        );
        var requestBytes = ProtocolSerializer.SerializeSessionRequest(request);
        await ProtocolSerializer.WriteMessageAsync(_stream, MessageType.SessionRequest, requestBytes, ct).ConfigureAwait(false);
        await _stream.FlushAsync(ct).ConfigureAwait(false);

        // Receive SessionResponse
        var (msgType, payload) = await ProtocolSerializer.ReadMessageAsync(_stream, ct).ConfigureAwait(false);

        if (msgType != MessageType.SessionResponse)
        {
            _tcpClient.Dispose();
            _tcpClient = null;
            _stream = null;
            throw new InvalidOperationException($"Expected SessionResponse but received {msgType}.");
        }

        var response = ProtocolSerializer.DeserializeSessionResponse(payload);

        if (!response.Accepted)
        {
            _tcpClient.Dispose();
            _tcpClient = null;
            _stream = null;
            throw new InvalidOperationException(
                response.RejectReason ?? "Session rejected by agent.");
        }

        // Session accepted — start background frame receive loop
        _connected = true;
        _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _receiveTask = ReceiveLoopAsync(_receiveCts.Token);
    }

    public async Task DisconnectAsync()
    {
        if (!_connected || _stream is null)
            return;

        try
        {
            // Send SessionClose
            await _sendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await ProtocolSerializer.WriteMessageAsync(
                    _stream,
                    MessageType.SessionClose,
                    ReadOnlyMemory<byte>.Empty
                ).ConfigureAwait(false);
                await _stream.FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }

            // Wait briefly for SessionCloseAck (best-effort)
            using var ackCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                var (msgType, _) = await ProtocolSerializer.ReadMessageAsync(_stream, ackCts.Token).ConfigureAwait(false);
                // We got the ack (or some other message) — either way, we're done
            }
            catch
            {
                // Timeout or error waiting for ack — proceed with cleanup
            }
        }
        catch
        {
            // Ignore errors during graceful disconnect
        }
        finally
        {
            CleanupConnection();
        }
    }

    public async Task SendInputCommandAsync(InputCommand command)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_connected || _stream is null)
            throw new InvalidOperationException("Not connected.");

        var buffer = new byte[ProtocolSerializer.InputCommandSize];
        ProtocolSerializer.WriteInputCommand(buffer, command);

        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await ProtocolSerializer.WriteMessageAsync(
                _stream,
                MessageType.InputCommand,
                buffer
            ).ConfigureAwait(false);
            await _stream.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (_connected && !ct.IsCancellationRequested)
            {
                var (msgType, payload) = await ProtocolSerializer.ReadMessageAsync(_stream!, ct).ConfigureAwait(false);

                switch (msgType)
                {
                    case MessageType.FrameData:
                        HandleFrameData(payload);
                        break;

                    case MessageType.SessionCloseAck:
                        // Server acknowledged our close — stop receiving
                        return;

                    case MessageType.Heartbeat:
                        // No action needed
                        break;

                    default:
                        // Unknown message type, ignore
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
        }
        catch (EndOfStreamException)
        {
            // Connection closed by server
        }
        catch (IOException)
        {
            // Connection lost
        }
        catch (ObjectDisposedException)
        {
            // Stream disposed during shutdown
        }
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
        if (payload.Length < ProtocolSerializer.FrameHeaderSize)
            return; // Malformed frame, skip

        var header = ProtocolSerializer.ReadFrameHeader(payload);

        if (header.CompressedLength <= 0 ||
            ProtocolSerializer.FrameHeaderSize + header.CompressedLength > payload.Length)
            return; // Invalid compressed length, skip

        var jpegData = new byte[header.CompressedLength];
        Buffer.BlockCopy(payload, ProtocolSerializer.FrameHeaderSize, jpegData, 0, header.CompressedLength);

        var frame = new EncodedFrame(
            Data: jpegData,
            Length: header.CompressedLength,
            Width: header.Width,
            Height: header.Height,
            TimestampTicks: header.TimestampTicks,
            SequenceNumber: header.SequenceNumber
        );

        OnFrameReceived?.Invoke(frame);
    }

    private void CleanupConnection()
    {
        _connected = false;

        if (_receiveCts is not null)
        {
            _receiveCts.Cancel();
            _receiveCts.Dispose();
            _receiveCts = null;
        }

        _stream = null;

        if (_tcpClient is not null)
        {
            _tcpClient.Dispose();
            _tcpClient = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        CleanupConnection();
        _sendLock.Dispose();
    }
}
