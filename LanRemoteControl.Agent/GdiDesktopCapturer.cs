using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using LanRemoteControl.Shared;

namespace LanRemoteControl.Agent;

/// <summary>
/// 基于 GDI+ (System.Drawing) 的桌面捕获器。
/// 使用 Graphics.CopyFromScreen 捕获桌面，兼容性好，无需 DXGI 权限。
/// </summary>
public sealed class GdiDesktopCapturer : IDesktopCapturer
{
    private int _screenWidth;
    private int _screenHeight;
    private float _scaleFactor = 1.0f;
    private Bitmap? _bitmap;
    private byte[]? _pixelBuffer;
    private GCHandle _pinnedBuffer;
    private bool _disposed;

    public Resolution CurrentResolution =>
        new((int)(_screenWidth * _scaleFactor), (int)(_screenHeight * _scaleFactor));

    public GdiDesktopCapturer()
    {
        // Get primary screen size
        _screenWidth = GetSystemMetrics(0);  // SM_CXSCREEN
        _screenHeight = GetSystemMetrics(1); // SM_CYSCREEN
    }

    public CapturedFrame? CaptureNextFrame(int timeoutMs = 33)
    {
        if (_disposed) return null;

        try
        {
            int captureWidth = _screenWidth;
            int captureHeight = _screenHeight;

            if (captureWidth <= 0 || captureHeight <= 0)
                return null;

            // Reuse or create bitmap
            if (_bitmap == null || _bitmap.Width != captureWidth || _bitmap.Height != captureHeight)
            {
                FreePinnedResources();
                _bitmap?.Dispose();

                _bitmap = new Bitmap(captureWidth, captureHeight, PixelFormat.Format32bppArgb);
            }

            using (var g = Graphics.FromImage(_bitmap))
            {
                // Always capture full screen — scaling is handled by the encoder/protocol
                g.CopyFromScreen(0, 0, 0, 0, new Size(_screenWidth, _screenHeight));
            }

            // Lock bits to get raw pixel data pointer
            var rect = new Rectangle(0, 0, captureWidth, captureHeight);
            var bmpData = _bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            int stride = bmpData.Stride;
            int bufferSize = stride * captureHeight;

            // Copy to pinned buffer so we can safely return a pointer
            if (_pixelBuffer == null || _pixelBuffer.Length != bufferSize)
            {
                FreePinnedResources();
                _pixelBuffer = new byte[bufferSize];
                _pinnedBuffer = GCHandle.Alloc(_pixelBuffer, GCHandleType.Pinned);
            }

            Marshal.Copy(bmpData.Scan0, _pixelBuffer, 0, bufferSize);
            _bitmap.UnlockBits(bmpData);

            return new CapturedFrame(
                _pinnedBuffer.AddrOfPinnedObject(),
                captureWidth,
                captureHeight,
                stride,
                DateTime.UtcNow.Ticks);
        }
        catch
        {
            return null;
        }
    }

    public void ReduceResolution(float scaleFactor)
    {
        if (scaleFactor is <= 0f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(scaleFactor));
        _scaleFactor = scaleFactor;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        FreePinnedResources();
        _bitmap?.Dispose();
    }

    private void FreePinnedResources()
    {
        if (_pinnedBuffer.IsAllocated)
            _pinnedBuffer.Free();
        _pixelBuffer = null;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
