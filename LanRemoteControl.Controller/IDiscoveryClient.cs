using LanRemoteControl.Shared;

namespace LanRemoteControl.Controller;

/// <summary>设备发现客户端接口，通过 UDP 广播扫描局域网内的被控端</summary>
public interface IDiscoveryClient
{
    /// <summary>扫描局域网内的被控端设备</summary>
    /// <param name="timeout">扫描超时时间</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>发现的被控端设备列表</returns>
    Task<List<DiscoveredAgent>> ScanAsync(TimeSpan timeout, CancellationToken ct);
}
