using LanRemoteControl.Agent;

namespace LanRemoteControl.Tests;

public class AdaptiveFrameRateControllerTests
{
    [Fact]
    public void InitialState_ScaleFactorIsOne()
    {
        var controller = new AdaptiveFrameRateController();
        Assert.Equal(1.0f, controller.RecommendedScaleFactor);
    }

    [Fact]
    public void InitialState_CurrentFpsIsZero()
    {
        var controller = new AdaptiveFrameRateController();
        Assert.Equal(0f, controller.CurrentFps);
    }

    [Fact]
    public void SingleFrame_FpsRemainsZero()
    {
        var controller = new AdaptiveFrameRateController();
        controller.RecordFrame();
        Assert.Equal(0f, controller.CurrentFps);
    }

    [Fact]
    public void ShouldAdjustResolution_NoChangeInitially()
    {
        var controller = new AdaptiveFrameRateController();
        bool changed = controller.ShouldAdjustResolution(out float scale);
        Assert.False(changed);
        Assert.Equal(1.0f, scale);
    }

    [Fact]
    public void ShouldAdjustResolution_ReturnsTrueOnlyOnce()
    {
        var controller = new AdaptiveFrameRateController();

        // Simulate low FPS by recording frames slowly
        // We need at least 2 frames to compute FPS, and 5 consecutive low readings
        // to trigger reduction. We'll use Thread.Sleep to simulate slow frame rate.
        SimulateLowFps(controller, readings: 6);

        // First call should detect the change
        bool changed1 = controller.ShouldAdjustResolution(out float scale1);
        Assert.True(changed1);
        Assert.Equal(0.75f, scale1);

        // Second call should not report change
        bool changed2 = controller.ShouldAdjustResolution(out float scale2);
        Assert.False(changed2);
        Assert.Equal(0.75f, scale2);
    }

    [Fact]
    public void LowFps_ReducesScaleAfterConsecutiveReadings()
    {
        var controller = new AdaptiveFrameRateController();

        // Simulate sustained low FPS (5 consecutive low readings needed)
        SimulateLowFps(controller, readings: 6);

        Assert.Equal(0.75f, controller.RecommendedScaleFactor);
    }

    [Fact]
    public void LowFps_DoesNotReduceBeforeThreshold()
    {
        var controller = new AdaptiveFrameRateController();

        // Only 3 low readings - not enough to trigger
        SimulateLowFps(controller, readings: 3);

        Assert.Equal(1.0f, controller.RecommendedScaleFactor);
    }

    [Fact]
    public void HighFps_RestoresScaleAfterConsecutiveReadings()
    {
        var controller = new AdaptiveFrameRateController();

        // First reduce
        SimulateLowFps(controller, readings: 6);
        Assert.Equal(0.75f, controller.RecommendedScaleFactor);

        // Need enough fast frames to flush the sliding window (30 frames)
        // so that FPS calculation reflects the new rate, then 10 more
        // consecutive good readings to trigger restore.
        SimulateHighFps(controller, readings: 45);

        Assert.Equal(1.0f, controller.RecommendedScaleFactor);
    }

    [Fact]
    public void HighFps_DoesNotRestoreBeforeThreshold()
    {
        var controller = new AdaptiveFrameRateController();

        // First reduce with minimal low readings
        SimulateLowFps(controller, readings: 6);
        Assert.Equal(0.75f, controller.RecommendedScaleFactor);

        // Record a few fast frames - not enough to flush window and accumulate
        // 10 consecutive good readings
        SimulateHighFps(controller, readings: 5);

        Assert.Equal(0.75f, controller.RecommendedScaleFactor);
    }

    /// <summary>
    /// Simulate low FPS by recording frames with ~100ms intervals (≈10 FPS).
    /// </summary>
    private static void SimulateLowFps(AdaptiveFrameRateController controller, int readings)
    {
        for (int i = 0; i < readings; i++)
        {
            controller.RecordFrame();
            Thread.Sleep(100); // ~10 FPS
        }
    }

    /// <summary>
    /// Simulate high FPS by recording frames with ~20ms intervals (≈50 FPS).
    /// </summary>
    private static void SimulateHighFps(AdaptiveFrameRateController controller, int readings)
    {
        for (int i = 0; i < readings; i++)
        {
            controller.RecordFrame();
            Thread.Sleep(20); // ~50 FPS
        }
    }
}
