namespace LanRemoteControl.Shared;

/// <summary>帧数据头 — 25 字节固定二进制布局</summary>
public readonly record struct FrameHeader(
    uint SequenceNumber,
    int Width,
    int Height,
    int CompressedLength,
    long TimestampTicks
);
