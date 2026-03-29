using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LanRemoteControl.Shared;

namespace LanRemoteControl.Controller;

/// <summary>基于 WPF 内置 JPEG 解码器的帧解码器</summary>
public sealed class TurboJpegFrameDecoder : IFrameDecoder, IDisposable
{
    private const int BytesPerPixel = 4; // BGRA
    private DecodedFrame? _lastFrame;

    public DecodedFrame Decode(EncodedFrame encoded)
    {
        try
        {
            using var ms = new MemoryStream(encoded.Data, 0, encoded.Length);
            var decoder = new JpegBitmapDecoder(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var source = decoder.Frames[0];

            // Convert to Bgra32 if needed
            FormatConvertedBitmap converted;
            if (source.Format != PixelFormats.Bgra32)
            {
                converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            }
            else
            {
                converted = new FormatConvertedBitmap();
                converted.BeginInit();
                converted.Source = source;
                converted.DestinationFormat = PixelFormats.Bgra32;
                converted.EndInit();
            }

            int width = converted.PixelWidth;
            int height = converted.PixelHeight;
            int stride = width * BytesPerPixel;
            byte[] pixelData = new byte[stride * height];
            converted.CopyPixels(pixelData, stride, 0);

            var frame = new DecodedFrame(pixelData, width, height, stride);
            _lastFrame = frame;
            return frame;
        }
        catch
        {
            if (_lastFrame.HasValue)
                return _lastFrame.Value;

            int stride = encoded.Width * BytesPerPixel;
            return new DecodedFrame(new byte[stride * encoded.Height], encoded.Width, encoded.Height, stride);
        }
    }

    public void Dispose() { }
}
