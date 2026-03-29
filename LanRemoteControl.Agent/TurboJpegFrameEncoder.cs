using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using LanRemoteControl.Shared;

namespace LanRemoteControl.Agent;

/// <summary>基于 System.Drawing 的 JPEG 帧编码器</summary>
public sealed class TurboJpegFrameEncoder : IFrameEncoder, IDisposable
{
    private const int DefaultQuality = 70;
    private const int MinQuality = 30;
    private const int MaxQuality = 100;

    private readonly ImageCodecInfo _jpegCodec;
    private uint _sequenceNumber;
    private int _quality = DefaultQuality;

    public TurboJpegFrameEncoder()
    {
        _jpegCodec = ImageCodecInfo.GetImageEncoders()
            .First(c => c.FormatID == ImageFormat.Jpeg.Guid);
    }

    public int Quality
    {
        get => _quality;
        set => _quality = Math.Clamp(value, MinQuality, MaxQuality);
    }

    public EncodedFrame Encode(CapturedFrame frame)
    {
        // Create Bitmap from raw BGRA pointer
        using var bitmap = new Bitmap(
            frame.Width, frame.Height,
            frame.Stride,
            PixelFormat.Format32bppArgb,
            frame.DataPointer);

        // Encode to JPEG in memory
        using var ms = new MemoryStream();
        var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)_quality);
        bitmap.Save(ms, _jpegCodec, encoderParams);

        byte[] jpegData = ms.ToArray();
        uint seq = _sequenceNumber++;

        return new EncodedFrame(
            Data: jpegData,
            Length: jpegData.Length,
            Width: frame.Width,
            Height: frame.Height,
            TimestampTicks: frame.TimestampTicks,
            SequenceNumber: seq);
    }

    public void Dispose() { }
}
