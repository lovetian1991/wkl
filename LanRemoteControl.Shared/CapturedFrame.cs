namespace LanRemoteControl.Shared;

/// <summary>捕获的原始桌面帧</summary>
public readonly record struct CapturedFrame(
    nint DataPointer,
    int Width,
    int Height,
    int Stride,
    long TimestampTicks
);
