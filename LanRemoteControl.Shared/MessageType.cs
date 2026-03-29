namespace LanRemoteControl.Shared;

/// <summary>网络协议消息类型</summary>
public enum MessageType : byte
{
    // 设备发现（UDP）
    DiscoveryRequest  = 0x01,
    DiscoveryResponse = 0x02,

    // 会话管理（TCP）
    SessionRequest    = 0x10,
    SessionResponse   = 0x11,
    SessionClose      = 0x12,
    SessionCloseAck   = 0x13,
    Heartbeat         = 0x14,

    // 数据传输（TCP）
    FrameData         = 0x20,
    InputCommand      = 0x30,
}
