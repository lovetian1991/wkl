using LanRemoteControl.Shared;

namespace LanRemoteControl.Agent;

/// <summary>通信服务端接口，管理 TCP 连接和消息收发</summary>
public interface ICommunicationServer : IDisposable
{
    event Action<SessionContext>? OnClientConnected;
    event Action<SessionContext>? OnClientDisconnected;

    Task StartAsync(int port, CancellationToken ct);
    Task StopAsync();
    Task SendFrameAsync(SessionContext session, EncodedFrame frame);
}
