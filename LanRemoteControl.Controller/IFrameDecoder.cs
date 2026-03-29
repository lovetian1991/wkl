using LanRemoteControl.Shared;

namespace LanRemoteControl.Controller;

/// <summary>帧解码器接口</summary>
public interface IFrameDecoder
{
    /// <summary>将编码帧解码为原始像素数据</summary>
    DecodedFrame Decode(EncodedFrame encoded);
}
