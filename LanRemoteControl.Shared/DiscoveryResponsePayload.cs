namespace LanRemoteControl.Shared;

/// <summary>设备发现响应载荷</summary>
public record DiscoveryResponsePayload(string HostName, int TcpPort);
