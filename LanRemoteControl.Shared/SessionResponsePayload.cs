namespace LanRemoteControl.Shared;

/// <summary>会话响应载荷</summary>
public record SessionResponsePayload(
    bool Accepted,
    string? RejectReason,
    int DesktopWidth,
    int DesktopHeight
);
