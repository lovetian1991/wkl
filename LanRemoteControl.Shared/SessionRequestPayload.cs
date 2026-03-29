namespace LanRemoteControl.Shared;

/// <summary>会话请求载荷</summary>
public record SessionRequestPayload(string ControllerName, int ProtocolVersion);
