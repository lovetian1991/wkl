namespace LanRemoteControl.Agent;

/// <summary>设备发现响应器接口</summary>
public interface IDiscoveryResponder : IDisposable
{
    Task StartAsync(CancellationToken ct);
}
