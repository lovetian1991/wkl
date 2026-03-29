namespace LanRemoteControl.Agent;

/// <summary>
/// 自适应带宽/压缩质量控制器。
/// 监控网络传输延迟和带宽，当带宽低于阈值时降低 JPEG 质量，恢复后还原默认质量。
/// </summary>
public class AdaptiveBandwidthController
{
    private const int SlidingWindowSize = 20;
    private const int ConsecutiveLowToReduce = 3;
    private const int ConsecutiveGoodToRestore = 5;

    private readonly Queue<(int BytesSent, double DurationSeconds)> _samples = new();
    private int _consecutiveLowCount;
    private int _consecutiveGoodCount;
    private int _recommendedQuality;

    /// <summary>默认 JPEG 质量</summary>
    public int DefaultQuality { get; } = 70;

    /// <summary>最低 JPEG 质量</summary>
    public int MinQuality { get; } = 30;

    /// <summary>当前推荐的 JPEG 质量值</summary>
    public int RecommendedQuality => _recommendedQuality;

    /// <summary>估算的当前带宽 (Mbps)</summary>
    public float EstimatedBandwidthMbps { get; private set; }

    /// <summary>带宽阈值 (Mbps)，低于此值时降低质量</summary>
    public float BandwidthThresholdMbps { get; set; } = 5.0f;

    public AdaptiveBandwidthController()
    {
        _recommendedQuality = DefaultQuality;
    }

    /// <summary>
    /// 每帧发送完成后调用，记录发送字节数和耗时，更新带宽估算和推荐质量。
    /// </summary>
    /// <param name="bytesSent">本次发送的字节数</param>
    /// <param name="sendDuration">本次发送耗时</param>
    public void RecordFrameSent(int bytesSent, TimeSpan sendDuration)
    {
        double durationSeconds = sendDuration.TotalSeconds;
        if (durationSeconds <= 0 || bytesSent <= 0)
            return;

        _samples.Enqueue((bytesSent, durationSeconds));

        while (_samples.Count > SlidingWindowSize)
        {
            _samples.Dequeue();
        }

        UpdateBandwidthEstimate();
        UpdateAdaptiveState();
    }

    private void UpdateBandwidthEstimate()
    {
        if (_samples.Count == 0)
        {
            EstimatedBandwidthMbps = 0f;
            return;
        }

        long totalBytes = 0;
        double totalSeconds = 0;

        foreach (var (bytes, duration) in _samples)
        {
            totalBytes += bytes;
            totalSeconds += duration;
        }

        if (totalSeconds > 0)
        {
            // Convert bytes/second to Mbps (megabits per second)
            double bytesPerSecond = totalBytes / totalSeconds;
            EstimatedBandwidthMbps = (float)(bytesPerSecond * 8.0 / 1_000_000.0);
        }
    }

    private void UpdateAdaptiveState()
    {
        if (EstimatedBandwidthMbps < BandwidthThresholdMbps)
        {
            _consecutiveLowCount++;
            _consecutiveGoodCount = 0;

            if (_consecutiveLowCount >= ConsecutiveLowToReduce
                && _recommendedQuality != MinQuality)
            {
                _recommendedQuality = MinQuality;
            }
        }
        else
        {
            _consecutiveGoodCount++;
            _consecutiveLowCount = 0;

            if (_consecutiveGoodCount >= ConsecutiveGoodToRestore
                && _recommendedQuality != DefaultQuality)
            {
                _recommendedQuality = DefaultQuality;
            }
        }
    }
}
