using LanRemoteControl.Shared;

namespace LanRemoteControl.Controller;

/// <summary>通信客户端接口，管理与被控端的 TCP 连接和消息收发</summary>
public interface ICommunicationClient : IDisposable
{
    /// <summary>接收到帧数据时触发</summary>
    event Action<EncodedFrame>? OnFrameReceived;

    /// <summary>连接断开时触发</summary>
    event Action? OnDisconnected;

    /// <summary>连接到被控端</summary>
    Task ConnectAsync(string host, int port, CancellationToken ct);

    /// <summary>主动断开连接</summary>
    Task DisconnectAsync();

    /// <summary>发送输入指令到被控端</summary>
    Task SendInputCommandAsync(InputCommand command);

    /// <summary>当前是否已连接</summary>
    bool IsConnected { get; }
}
