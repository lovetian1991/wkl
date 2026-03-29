namespace LanRemoteControl.Shared;

/// <summary>帧数据头 — 24 字节固定二进制布局 (4+4+4+4+8)</summary>
public readonly record struct FrameHeader(
    uint SequenceNumber,
    int Width,
    int Height,
    int CompressedLength,
    long TimestampTicks
);
