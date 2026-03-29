using System.Net;
using System.Net.Sockets;
using LanRemoteControl.Shared;

namespace LanRemoteControl.Agent;

/// <summary>
/// 基于原生 TCP Socket 的通信服务端实现。
/// 负责监听 TCP 连接、会话握手、帧数据发送和输入指令接收分发。
/// </summary>
public class CommunicationServer : ICommunicationServer
{
    private readonly ISessionManager _sessionManager;
    private readonly IInputSimulator _inputSimulator;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private bool _disposed;

    public event Action<SessionContext>? OnClientConnected;
    public event Action<SessionContext>? OnClientDisconnected;

    public CommunicationServer(ISessionManager sessionManager, IInputSimulator inputSimulator)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _inputSimulator = inputSimulator ?? throw new ArgumentNullException(nameof(inputSimulator));
    }

    public Task StartAsync(int port, CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _acceptTask = AcceptClientsAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
        }

        _listener?.Stop();

        if (_acceptTask is not null)
        {
            try { await _acceptTask; } catch (OperationCanceledException) { }
        }
    }

    public async Task SendFrameAsync(SessionContext session, EncodedFrame frame)
    {
        if (!session.IsActive)
            return;

        try
        {
            var header = new FrameHeader(
                frame.SequenceNumber,
                frame.Width,
                frame.Height,
                frame.Length,
                frame.TimestampTicks
            );

            var payload = new byte[ProtocolSerializer.FrameHeaderSize + frame.Length];
            ProtocolSerializer.WriteFrameHeader(payload, header);
            Buffer.BlockCopy(frame.Data, 0, payload, ProtocolSerializer.FrameHeaderSize, frame.Length);

            await ProtocolSerializer.WriteMessageAsync(
                session.Stream,
                MessageType.FrameData,
                payload
            ).ConfigureAwait(false);

            await session.Stream.FlushAsync().ConfigureAwait(false);
        }
        catch
        {
            // 写入失败说明连接已断，主动结束会话
            if (session.IsActive)
            {
                _sessionManager.EndSession(session.SessionId);
                OnClientDisconnected?.Invoke(session);
            }
        }
    }

    private async Task AcceptClientsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            // Handle each client connection in a background task
            client.NoDelay = true;
            // TCP KeepAlive: 检测死连接
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 10);      // 10秒无数据开始探测
            client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 5);   // 每5秒探测一次
            client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3); // 3次失败判定断开
            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        NetworkStream stream = client.GetStream();
        SessionContext? session = null;

        try
        {
            // Step 1: Read first message — must be SessionRequest
            var (msgType, payload) = await ProtocolSerializer.ReadMessageAsync(stream, ct).ConfigureAwait(false);

            if (msgType != MessageType.SessionRequest)
            {
                // Unexpected message type, close connection
                client.Dispose();
                return;
            }

            // Step 2: Deserialize SessionRequestPayload
            var request = ProtocolSerializer.DeserializeSessionRequest(payload);

            // Step 3: Create SessionContext
            session = new SessionContext
            {
                SessionId = Guid.NewGuid().ToString("N"),
                TcpClient = client,
                Stream = stream
            };

            // Step 4: Try to accept session via SessionManager
            bool accepted = _sessionManager.TryAcceptSession(session);

            // Step 5: Send SessionResponse
            var response = new SessionResponsePayload(
                Accepted: accepted,
                RejectReason: accepted ? null : "Agent already has an active session.",
                DesktopWidth: 0,
                DesktopHeight: 0
            );

            var responseBytes = ProtocolSerializer.SerializeSessionResponse(response);
            await ProtocolSerializer.WriteMessageAsync(stream, MessageType.SessionResponse, responseBytes, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);

            if (!accepted)
            {
                client.Dispose();
                return;
            }

            // Step 6: Fire OnClientConnected
            OnClientConnected?.Invoke(session);

            // Step 7: Start receiving InputCommand messages
            await ReceiveInputLoopAsync(session, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
        }
        catch (Exception)
        {
            // Connection error (e.g., client disconnected)
        }
        finally
        {
            // Clean up session on disconnect or error
            if (session is not null && session.IsActive)
            {
                _sessionManager.EndSession(session.SessionId);
                OnClientDisconnected?.Invoke(session);
            }
        }
    }

    private async Task ReceiveInputLoopAsync(SessionContext session, CancellationToken ct)
    {
        while (session.IsActive && !ct.IsCancellationRequested)
        {
            var (msgType, payload) = await ProtocolSerializer.ReadMessageAsync(session.Stream, ct).ConfigureAwait(false);

            switch (msgType)
            {
                case MessageType.InputCommand:
                    var command = ProtocolSerializer.ReadInputCommand(payload);
                    _inputSimulator.ExecuteCommand(command);
                    break;

                case MessageType.SessionClose:
                    // Client requested session close — acknowledge and exit
                    await ProtocolSerializer.WriteMessageAsync(
                        session.Stream,
                        MessageType.SessionCloseAck,
                        ReadOnlyMemory<byte>.Empty,
                        ct
                    ).ConfigureAwait(false);
                    return;

                case MessageType.Heartbeat:
                    // Heartbeat received, no action needed
                    break;

                default:
                    // Unknown message type, ignore
                    break;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();
        _listener?.Stop();
        _cts?.Dispose();
    }
}
