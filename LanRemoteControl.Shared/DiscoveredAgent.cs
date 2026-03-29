namespace LanRemoteControl.Shared;

/// <summary>发现的被控端设备</summary>
public record DiscoveredAgent(string HostName, string IpAddress, int Port);
