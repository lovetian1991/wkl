using LanRemoteControl.Shared;

namespace LanRemoteControl.Agent;

/// <summary>桌面捕获器接口</summary>
public interface IDesktopCapturer : IDisposable
{
    /// <summary>捕获下一帧桌面画面</summary>
    /// <param name="timeoutMs">等待超时（毫秒），默认33ms对应30fps</param>
    /// <returns>捕获的帧数据，null 表示无变化</returns>
    CapturedFrame? CaptureNextFrame(int timeoutMs = 33);

    /// <summary>当前捕获分辨率</summary>
    Resolution CurrentResolution { get; }

    /// <summary>降低捕获分辨率（资源不足时按缩放因子缩小）</summary>
    /// <param name="scaleFactor">缩放因子，例如 0.75</param>
    void ReduceResolution(float scaleFactor);
}
