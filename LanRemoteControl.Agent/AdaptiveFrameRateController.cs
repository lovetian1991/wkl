using System.Diagnostics;

namespace LanRemoteControl.Agent;

/// <summary>
/// 自适应帧率/分辨率控制器。
/// 监控当前帧率，当帧率持续低于阈值时降低分辨率，恢复后还原。
/// </summary>
public class AdaptiveFrameRateController
{
    private const int SlidingWindowSize = 30;
    private const float LowFpsThreshold = 20f;
    private const float ReducedScaleFactor = 0.75f;
    private const float FullScaleFactor = 1.0f;
    private const int ConsecutiveLowToReduce = 5;
    private const int ConsecutiveGoodToRestore = 10;

    private readonly Queue<long> _frameTimestamps = new();
    private int _consecutiveLowCount;
    private int _consecutiveGoodCount;
    private float _currentScaleFactor = FullScaleFactor;
    private bool _scaleFactorChanged;

    /// <summary>当前测量的帧率 (FPS)</summary>
    public float CurrentFps { get; private set; }

    /// <summary>推荐的缩放因子 (1.0 = 原始分辨率, 0.75 = 降低分辨率)</summary>
    public float RecommendedScaleFactor => _currentScaleFactor;

    /// <summary>
    /// 每次帧捕获后调用，记录帧时间戳并更新帧率计算。
    /// </summary>
    public void RecordFrame()
    {
        long now = Stopwatch.GetTimestamp();
        _frameTimestamps.Enqueue(now);

        // 保持滑动窗口大小
        while (_frameTimestamps.Count > SlidingWindowSize)
        {
            _frameTimestamps.Dequeue();
        }

        // 至少需要 2 帧才能计算帧率
        if (_frameTimestamps.Count < 2)
        {
            CurrentFps = 0f;
            return;
        }

        long oldest = _frameTimestamps.Peek();
        double elapsedSeconds = (now - oldest) / (double)Stopwatch.Frequency;

        if (elapsedSeconds > 0)
        {
            CurrentFps = (float)((_frameTimestamps.Count - 1) / elapsedSeconds);
        }

        UpdateAdaptiveState();
    }

    /// <summary>
    /// 检查是否需要调整分辨率。
    /// </summary>
    /// <param name="scaleFactor">输出当前推荐的缩放因子</param>
    /// <returns>如果缩放因子发生了变化则返回 true</returns>
    public bool ShouldAdjustResolution(out float scaleFactor)
    {
        scaleFactor = _currentScaleFactor;
        if (_scaleFactorChanged)
        {
            _scaleFactorChanged = false;
            return true;
        }
        return false;
    }

    private void UpdateAdaptiveState()
    {
        if (CurrentFps < LowFpsThreshold)
        {
            _consecutiveLowCount++;
            _consecutiveGoodCount = 0;

            // 持续低帧率达到阈值，触发分辨率降低
            if (_consecutiveLowCount >= ConsecutiveLowToReduce
                && _currentScaleFactor != ReducedScaleFactor)
            {
                _currentScaleFactor = ReducedScaleFactor;
                _scaleFactorChanged = true;
            }
        }
        else
        {
            _consecutiveGoodCount++;
            _consecutiveLowCount = 0;

            // 持续恢复达到阈值，还原分辨率
            if (_consecutiveGoodCount >= ConsecutiveGoodToRestore
                && _currentScaleFactor != FullScaleFactor)
            {
                _currentScaleFactor = FullScaleFactor;
                _scaleFactorChanged = true;
            }
        }
    }
}
