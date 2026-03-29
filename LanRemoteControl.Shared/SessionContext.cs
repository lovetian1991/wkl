using System.Net.Sockets;

namespace LanRemoteControl.Shared;

/// <summary>会话上下文</summary>
public class SessionContext
{
    public required string SessionId { get; init; }
    public required TcpClient TcpClient { get; init; }
    public required NetworkStream Stream { get; init; }
    public bool IsActive { get; set; }
}
