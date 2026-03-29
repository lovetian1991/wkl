namespace LanRemoteControl.Shared;

/// <summary>编码后的帧数据</summary>
public readonly record struct EncodedFrame(
    byte[] Data,
    int Length,
    int Width,
    int Height,
    long TimestampTicks,
    uint SequenceNumber
);
