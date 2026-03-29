using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using LanRemoteControl.Shared;

namespace LanRemoteControl.Controller;

/// <summary>
/// 基于 UDP 广播的设备发现客户端实现。
/// 发送 DiscoveryRequest 广播到端口 19620，收集 DiscoveryResponse 响应。
/// </summary>
public sealed class DiscoveryClient : IDiscoveryClient
{
    public const int DiscoveryPort = 19620;

    public async Task<List<DiscoveredAgent>> ScanAsync(TimeSpan timeout, CancellationToken ct)
    {
        var agents = new List<DiscoveredAgent>();

        using var udpClient = new UdpClient();
        udpClient.EnableBroadcast = true;

        // Build DiscoveryRequest message: 1 byte MessageType + 4 bytes PayloadLength (LE) + empty payload
        var requestMessage = new byte[ProtocolSerializer.MessageHeaderSize];
        requestMessage[0] = (byte)MessageType.DiscoveryRequest;
        BinaryPrimitives.WriteInt32LittleEndian(requestMessage.AsSpan(1), 0);

        var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
        await udpClient.SendAsync(requestMessage, requestMessage.Length, broadcastEndpoint).ConfigureAwait(false);

        // Collect responses until timeout
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            while (!timeoutCts.Token.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await udpClient.ReceiveAsync(timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (result.Buffer.Length < ProtocolSerializer.MessageHeaderSize)
                    continue;

                var messageType = (MessageType)result.Buffer[0];
                if (messageType != MessageType.DiscoveryResponse)
                    continue;

                int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(result.Buffer.AsSpan(1));
                if (result.Buffer.Length < ProtocolSerializer.MessageHeaderSize + payloadLength)
                    continue;

                try
                {
                    var payloadBytes = new byte[payloadLength];
                    Buffer.BlockCopy(result.Buffer, ProtocolSerializer.MessageHeaderSize, payloadBytes, 0, payloadLength);

                    var response = ProtocolSerializer.DeserializeDiscoveryResponse(payloadBytes);

                    var agent = new DiscoveredAgent(
                        HostName: response.HostName,
                        IpAddress: result.RemoteEndPoint.Address.ToString(),
                        Port: response.TcpPort
                    );

                    agents.Add(agent);
                }
                catch
                {
                    // Skip malformed responses
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout reached — return what we have
        }

        return agents;
    }
}
