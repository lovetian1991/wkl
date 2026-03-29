using LanRemoteControl.Shared;
using TurboJpegWrapper;

namespace LanRemoteControl.Agent;

/// <summary>基于 TurboJPEG (libjpeg-turbo) 的帧编码器</summary>
public sealed class TurboJpegFrameEncoder : IFrameEncoder, IDisposable
{
    private const int DefaultQuality = 70;
    private const int MinQuality = 30;
    private const int MaxQuality = 100;

    private readonly TJCompressor _compressor;
    private uint _sequenceNumber;
    private int _quality = DefaultQuality;

    public TurboJpegFrameEncoder()
    {
        _compressor = new TJCompressor();
    }

    /// <inheritdoc/>
    public int Quality
    {
        get => _quality;
        set => _quality = Math.Clamp(value, MinQuality, MaxQuality);
    }

    /// <inheritdoc/>
    public EncodedFrame Encode(CapturedFrame frame)
    {
        // Use the IntPtr overload directly — CapturedFrame.DataPointer
        // points to BGRA pixel data, CapturedFrame.Stride is the row pitch.
        byte[] jpegData = _compressor.Compress(
            frame.DataPointer,
            frame.Stride,
            frame.Width,
            frame.Height,
            TJPixelFormats.TJPF_BGRA,
            TJSubsamplingOptions.TJSAMP_420,
            _quality,
            TJFlags.NONE);

        uint seq = _sequenceNumber++;

        return new EncodedFrame(
            Data: jpegData,
            Length: jpegData.Length,
            Width: frame.Width,
            Height: frame.Height,
            TimestampTicks: frame.TimestampTicks,
            SequenceNumber: seq);
    }

    public void Dispose()
    {
        _compressor.Dispose();
    }
}
