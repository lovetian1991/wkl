using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using LanRemoteControl.Shared;

namespace LanRemoteControl.Agent;

/// <summary>
/// UDP 设备发现响应器。
/// 监听 UDP 端口 19620，收到 DiscoveryRequest 后回复包含主机名和 TCP 服务端口的 DiscoveryResponse。
/// </summary>
public sealed class DiscoveryResponder : IDiscoveryResponder
{
    public const int DiscoveryPort = 19620;

    private readonly int _tcpPort;
    private UdpClient? _udpClient;
    private bool _disposed;

    public DiscoveryResponder(int tcpPort)
    {
        _tcpPort = tcpPort;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _udpClient = new UdpClient(DiscoveryPort);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await _udpClient.ReceiveAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (result.Buffer.Length < ProtocolSerializer.MessageHeaderSize)
                    continue;

                var messageType = (MessageType)result.Buffer[0];
                if (messageType != MessageType.DiscoveryRequest)
                    continue;

                var responsePayload = new DiscoveryResponsePayload(
                    Environment.MachineName,
                    _tcpPort
                );

                byte[] jsonPayload = ProtocolSerializer.SerializeDiscoveryResponse(responsePayload);

                // Build UDP message: 1 byte MessageType + 4 bytes PayloadLength (LE) + Payload
                byte[] responseMessage = new byte[ProtocolSerializer.MessageHeaderSize + jsonPayload.Length];
                responseMessage[0] = (byte)MessageType.DiscoveryResponse;
                BinaryPrimitives.WriteInt32LittleEndian(responseMessage.AsSpan(1), jsonPayload.Length);
                Buffer.BlockCopy(jsonPayload, 0, responseMessage, ProtocolSerializer.MessageHeaderSize, jsonPayload.Length);

                await _udpClient.SendAsync(responseMessage, responseMessage.Length, result.RemoteEndPoint).ConfigureAwait(false);
            }
        }
        catch (ObjectDisposedException) when (_disposed || ct.IsCancellationRequested)
        {
            // Expected during shutdown
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _udpClient?.Dispose();
    }
}
