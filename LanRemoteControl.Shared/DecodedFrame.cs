namespace LanRemoteControl.Shared;

/// <summary>解码后的帧数据</summary>
public readonly record struct DecodedFrame(
    byte[] PixelData,
    int Width,
    int Height,
    int Stride
);
