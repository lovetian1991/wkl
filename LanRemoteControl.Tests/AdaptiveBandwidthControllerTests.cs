using LanRemoteControl.Agent;

namespace LanRemoteControl.Tests;

public class AdaptiveBandwidthControllerTests
{
    [Fact]
    public void InitialState_RecommendedQualityIsDefault()
    {
        var controller = new AdaptiveBandwidthController();
        Assert.Equal(70, controller.RecommendedQuality);
        Assert.Equal(70, controller.DefaultQuality);
        Assert.Equal(30, controller.MinQuality);
    }

    [Fact]
    public void InitialState_EstimatedBandwidthIsZero()
    {
        var controller = new AdaptiveBandwidthController();
        Assert.Equal(0f, controller.EstimatedBandwidthMbps);
    }

    [Fact]
    public void HighBandwidth_QualityRemainsDefault()
    {
        var controller = new AdaptiveBandwidthController { BandwidthThresholdMbps = 5.0f };

        // Simulate high bandwidth: 1MB in 10ms = ~800 Mbps
        for (int i = 0; i < 10; i++)
        {
            controller.RecordFrameSent(1_000_000, TimeSpan.FromMilliseconds(10));
        }

        Assert.Equal(70, controller.RecommendedQuality);
        Assert.True(controller.EstimatedBandwidthMbps > 5.0f);
    }

    [Fact]
    public void LowBandwidth_QualityReducedToMinimum()
    {
        var controller = new AdaptiveBandwidthController { BandwidthThresholdMbps = 5.0f };

        // Simulate low bandwidth: 1KB in 1 second = ~0.008 Mbps
        for (int i = 0; i < 5; i++)
        {
            controller.RecordFrameSent(1_000, TimeSpan.FromSeconds(1));
        }

        Assert.Equal(30, controller.RecommendedQuality);
        Assert.True(controller.EstimatedBandwidthMbps < 5.0f);
    }

    [Fact]
    public void LowBandwidth_DoesNotReduceBeforeConsecutiveThreshold()
    {
        var controller = new AdaptiveBandwidthController { BandwidthThresholdMbps = 5.0f };

        // Only 2 low bandwidth samples - not enough (need 3 consecutive)
        controller.RecordFrameSent(1_000, TimeSpan.FromSeconds(1));
        controller.RecordFrameSent(1_000, TimeSpan.FromSeconds(1));

        Assert.Equal(70, controller.RecommendedQuality);
    }

    [Fact]
    public void BandwidthRecovery_QualityRestoresToDefault()
    {
        var controller = new AdaptiveBandwidthController { BandwidthThresholdMbps = 5.0f };

        // First reduce quality with low bandwidth
        for (int i = 0; i < 5; i++)
        {
            controller.RecordFrameSent(1_000, TimeSpan.FromSeconds(1));
        }
        Assert.Equal(30, controller.RecommendedQuality);

        // Recover with high bandwidth (need to flush window + 5 consecutive good)
        for (int i = 0; i < 25; i++)
        {
            controller.RecordFrameSent(1_000_000, TimeSpan.FromMilliseconds(10));
        }

        Assert.Equal(70, controller.RecommendedQuality);
    }

    [Fact]
    public void ZeroDuration_IsIgnored()
    {
        var controller = new AdaptiveBandwidthController();
        controller.RecordFrameSent(1000, TimeSpan.Zero);
        Assert.Equal(0f, controller.EstimatedBandwidthMbps);
        Assert.Equal(70, controller.RecommendedQuality);
    }

    [Fact]
    public void ZeroBytes_IsIgnored()
    {
        var controller = new AdaptiveBandwidthController();
        controller.RecordFrameSent(0, TimeSpan.FromMilliseconds(10));
        Assert.Equal(0f, controller.EstimatedBandwidthMbps);
        Assert.Equal(70, controller.RecommendedQuality);
    }

    [Fact]
    public void BandwidthThreshold_IsConfigurable()
    {
        var controller = new AdaptiveBandwidthController { BandwidthThresholdMbps = 100.0f };

        // 1MB in 10ms = ~800 Mbps, but threshold is 100 Mbps so this is fine
        for (int i = 0; i < 10; i++)
        {
            controller.RecordFrameSent(1_000_000, TimeSpan.FromMilliseconds(10));
        }

        Assert.Equal(70, controller.RecommendedQuality);
    }
}
