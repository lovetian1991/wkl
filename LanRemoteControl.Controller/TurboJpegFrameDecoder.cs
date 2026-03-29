using System.Runtime.InteropServices;
using LanRemoteControl.Shared;
using TurboJpegWrapper;

namespace LanRemoteControl.Controller;

/// <summary>基于 TurboJPEG (libjpeg-turbo) 的帧解码器</summary>
public sealed class TurboJpegFrameDecoder : IFrameDecoder, IDisposable
{
    private const TJPixelFormats PixelFormat = TJPixelFormats.TJPF_BGRA;
    private const int BytesPerPixel = 4; // BGRA

    private readonly TJDecompressor _decompressor;
    private DecodedFrame? _lastFrame;

    public TurboJpegFrameDecoder()
    {
        _decompressor = new TJDecompressor();
    }

    /// <inheritdoc/>
    public DecodedFrame Decode(EncodedFrame encoded)
    {
        try
        {
            DecompressedImage result;
            var handle = GCHandle.Alloc(encoded.Data, GCHandleType.Pinned);
            try
            {
                result = _decompressor.Decompress(
                    handle.AddrOfPinnedObject(),
                    (ulong)encoded.Length,
                    PixelFormat,
                    TJFlags.NONE);
            }
            finally
            {
                handle.Free();
            }

            byte[] pixelData = result.Data;
            int stride = encoded.Width * BytesPerPixel;

            var frame = new DecodedFrame(
                PixelData: pixelData,
                Width: encoded.Width,
                Height: encoded.Height,
                Stride: stride);

            _lastFrame = frame;
            return frame;
        }
        catch
        {
            // On decode failure, return the last successfully decoded frame
            if (_lastFrame.HasValue)
            {
                return _lastFrame.Value;
            }

            // No previous frame — return a black frame of the expected size
            int stride = encoded.Width * BytesPerPixel;
            int size = stride * encoded.Height;
            byte[] blackPixels = new byte[size]; // default zero = black in BGRA

            return new DecodedFrame(
                PixelData: blackPixels,
                Width: encoded.Width,
                Height: encoded.Height,
                Stride: stride);
        }
    }

    public void Dispose()
    {
        _decompressor.Dispose();
    }
}
