using System.Runtime.InteropServices;
using LanRemoteControl.Shared;

namespace LanRemoteControl.Agent;

/// <summary>
/// DXGI Desktop Duplication API 桌面捕获器实现。
/// 使用 COM interop 直接调用 DXGI/D3D11 API，无需第三方依赖。
/// </summary>
public sealed class DxgiDesktopCapturer : IDesktopCapturer
{
    private const int MaxInitRetries = 5;
    private const int InitRetryDelayMs = 1000;
    private const int MaxConsecutiveTimeouts = 10;

    private nint _device;
    private nint _deviceContext;
    private nint _outputDuplication;
    private nint _stagingTexture;

    private int _outputWidth;
    private int _outputHeight;
    private float _scaleFactor = 1.0f;
    private int _consecutiveTimeouts;
    private bool _disposed;

    public Resolution CurrentResolution =>
        new((int)(_outputWidth * _scaleFactor), (int)(_outputHeight * _scaleFactor));

    public DxgiDesktopCapturer()
    {
        InitializeDxgiResources();
    }

    public CapturedFrame? CaptureNextFrame(int timeoutMs = 33)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_outputDuplication == nint.Zero)
            return null;

        var hr = NativeMethods.IDXGIOutputDuplication_AcquireNextFrame(
            _outputDuplication, (uint)timeoutMs, out var frameInfo, out var desktopResource);

        if (hr == NativeMethods.DXGI_ERROR_WAIT_TIMEOUT)
        {
            _consecutiveTimeouts++;
            if (_consecutiveTimeouts >= MaxConsecutiveTimeouts)
            {
                _consecutiveTimeouts = 0;
                ReinitializeDxgiResources();
            }
            return null;
        }

        if (hr < 0)
        {
            // Access lost or other error — reinitialize
            ReinitializeDxgiResources();
            return null;
        }

        _consecutiveTimeouts = 0;

        try
        {
            // QI for ID3D11Texture2D from the desktop resource
            var iidTexture2D = NativeMethods.IID_ID3D11Texture2D;
            hr = Marshal.QueryInterface(desktopResource, ref iidTexture2D, out var acquiredTexture);
            if (hr < 0)
                return null;

            try
            {
                EnsureStagingTexture();

                // Copy acquired texture to staging texture
                NativeMethods.ID3D11DeviceContext_CopyResource(
                    _deviceContext, _stagingTexture, acquiredTexture);

                // Map the staging texture to get CPU-accessible pointer
                hr = NativeMethods.ID3D11DeviceContext_Map(
                    _deviceContext, _stagingTexture, 0,
                    NativeMethods.D3D11_MAP_READ, 0, out var mappedResource);

                if (hr < 0)
                    return null;

                return new CapturedFrame(
                    mappedResource.pData,
                    _outputWidth,
                    _outputHeight,
                    (int)mappedResource.RowPitch,
                    DateTime.UtcNow.Ticks);
            }
            finally
            {
                Marshal.Release(acquiredTexture);
            }
        }
        finally
        {
            NativeMethods.IDXGIOutputDuplication_ReleaseFrame(_outputDuplication);
            if (desktopResource != nint.Zero)
                Marshal.Release(desktopResource);
        }
    }

    public void ReduceResolution(float scaleFactor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (scaleFactor is <= 0f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(scaleFactor), "Scale factor must be between 0 (exclusive) and 1 (inclusive).");

        _scaleFactor = scaleFactor;
        ReleaseStagingTexture();
    }

    /// <summary>Unmap the staging texture after the caller is done reading frame data.</summary>
    public void UnmapStagingTexture()
    {
        if (_deviceContext != nint.Zero && _stagingTexture != nint.Zero)
            NativeMethods.ID3D11DeviceContext_Unmap(_deviceContext, _stagingTexture, 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ReleaseStagingTexture();

        if (_outputDuplication != nint.Zero)
        {
            Marshal.Release(_outputDuplication);
            _outputDuplication = nint.Zero;
        }
        if (_deviceContext != nint.Zero)
        {
            Marshal.Release(_deviceContext);
            _deviceContext = nint.Zero;
        }
        if (_device != nint.Zero)
        {
            Marshal.Release(_device);
            _device = nint.Zero;
        }
    }

    #region Private Helpers

    private void InitializeDxgiResources()
    {
        for (int attempt = 0; attempt < MaxInitRetries; attempt++)
        {
            try
            {
                CreateD3D11Device();
                CreateOutputDuplication();
                return;
            }
            catch
            {
                ReleaseAllResources();
                if (attempt < MaxInitRetries - 1)
                    Thread.Sleep(InitRetryDelayMs);
            }
        }

        // All retries exhausted — leave in uninitialized state.
        // The capturer will return null from CaptureNextFrame.
    }

    private void ReinitializeDxgiResources()
    {
        ReleaseAllResources();
        InitializeDxgiResources();
    }

    private void CreateD3D11Device()
    {
        var featureLevels = new[]
        {
            NativeMethods.D3D_FEATURE_LEVEL_11_0,
            NativeMethods.D3D_FEATURE_LEVEL_10_1,
            NativeMethods.D3D_FEATURE_LEVEL_10_0,
        };

        int hr = NativeMethods.D3D11CreateDevice(
            nint.Zero,
            NativeMethods.D3D_DRIVER_TYPE_HARDWARE,
            nint.Zero,
            0, // flags
            featureLevels,
            (uint)featureLevels.Length,
            NativeMethods.D3D11_SDK_VERSION,
            out _device,
            out _,
            out _deviceContext);

        Marshal.ThrowExceptionForHR(hr);
    }

    private void CreateOutputDuplication()
    {
        // Get IDXGIDevice from D3D11 device
        var iidDxgiDevice = NativeMethods.IID_IDXGIDevice;
        int hr = Marshal.QueryInterface(_device, ref iidDxgiDevice, out var dxgiDevice);
        Marshal.ThrowExceptionForHR(hr);

        try
        {
            // Get IDXGIAdapter
            hr = NativeMethods.IDXGIDevice_GetAdapter(dxgiDevice, out var adapter);
            Marshal.ThrowExceptionForHR(hr);

            try
            {
                // Get primary output (IDXGIOutput)
                hr = NativeMethods.IDXGIAdapter_EnumOutputs(adapter, 0, out var output);
                Marshal.ThrowExceptionForHR(hr);

                try
                {
                    // Get output description for resolution
                    hr = NativeMethods.IDXGIOutput_GetDesc(output, out var outputDesc);
                    Marshal.ThrowExceptionForHR(hr);

                    _outputWidth = outputDesc.DesktopCoordinates.Right - outputDesc.DesktopCoordinates.Left;
                    _outputHeight = outputDesc.DesktopCoordinates.Bottom - outputDesc.DesktopCoordinates.Top;

                    // QI for IDXGIOutput1
                    var iidOutput1 = NativeMethods.IID_IDXGIOutput1;
                    hr = Marshal.QueryInterface(output, ref iidOutput1, out var output1);
                    Marshal.ThrowExceptionForHR(hr);

                    try
                    {
                        // DuplicateOutput
                        hr = NativeMethods.IDXGIOutput1_DuplicateOutput(output1, _device, out _outputDuplication);
                        Marshal.ThrowExceptionForHR(hr);
                    }
                    finally
                    {
                        Marshal.Release(output1);
                    }
                }
                finally
                {
                    Marshal.Release(output);
                }
            }
            finally
            {
                Marshal.Release(adapter);
            }
        }
        finally
        {
            Marshal.Release(dxgiDevice);
        }
    }

    private void EnsureStagingTexture()
    {
        if (_stagingTexture != nint.Zero)
            return;

        // Staging texture must match the output duplication size exactly
        // (CopyResource requires identical dimensions)
        var desc = new NativeMethods.D3D11_TEXTURE2D_DESC
        {
            Width = (uint)_outputWidth,
            Height = (uint)_outputHeight,
            MipLevels = 1,
            ArraySize = 1,
            Format = NativeMethods.DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleDesc = new NativeMethods.DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
            Usage = NativeMethods.D3D11_USAGE_STAGING,
            BindFlags = 0,
            CPUAccessFlags = NativeMethods.D3D11_CPU_ACCESS_READ,
            MiscFlags = 0,
        };

        int hr = NativeMethods.ID3D11Device_CreateTexture2D(_device, ref desc, nint.Zero, out _stagingTexture);
        Marshal.ThrowExceptionForHR(hr);
    }

    private void ReleaseStagingTexture()
    {
        if (_stagingTexture != nint.Zero)
        {
            Marshal.Release(_stagingTexture);
            _stagingTexture = nint.Zero;
        }
    }

    private void ReleaseAllResources()
    {
        ReleaseStagingTexture();

        if (_outputDuplication != nint.Zero)
        {
            Marshal.Release(_outputDuplication);
            _outputDuplication = nint.Zero;
        }
        if (_deviceContext != nint.Zero)
        {
            Marshal.Release(_deviceContext);
            _deviceContext = nint.Zero;
        }
        if (_device != nint.Zero)
        {
            Marshal.Release(_device);
            _device = nint.Zero;
        }
    }

    #endregion
}
