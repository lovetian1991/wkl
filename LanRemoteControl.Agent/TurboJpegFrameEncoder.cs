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
    private readonly MemoryStream _reuseStream = new(1024 * 256); // 复用，减少 GC
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
        using var bitmap = new Bitmap(
            frame.Width, frame.Height,
            frame.Stride,
            PixelFormat.Format32bppArgb,
            frame.DataPointer);

        _reuseStream.SetLength(0);
        var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)_quality);
        bitmap.Save(_reuseStream, _jpegCodec, encoderParams);

        int length = (int)_reuseStream.Length;
        byte[] jpegData = new byte[length];
        _reuseStream.Position = 0;
        _reuseStream.ReadExactly(jpegData, 0, length);

        uint seq = _sequenceNumber++;

        return new EncodedFrame(
            Data: jpegData,
            Length: length,
            Width: frame.Width,
            Height: frame.Height,
            TimestampTicks: frame.TimestampTicks,
            SequenceNumber: seq);
    }

    public void Dispose()
    {
        _reuseStream.Dispose();
    }
}
