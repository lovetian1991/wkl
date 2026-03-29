using LanRemoteControl.Shared;

namespace LanRemoteControl.Agent;

/// <summary>帧编码器接口</summary>
public interface IFrameEncoder
{
    /// <summary>将捕获的原始帧编码为压缩格式</summary>
    EncodedFrame Encode(CapturedFrame frame);

    /// <summary>当前压缩质量 (30-100)</summary>
    int Quality { get; set; }
}
