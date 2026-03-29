using System.Diagnostics;
using LanRemoteControl.Shared;

namespace LanRemoteControl.Agent;

/// <summary>
/// 崩溃恢复日志记录器。
/// 使用文件计数器跟踪连续崩溃次数，超过阈值时写入 Windows 事件日志。
/// </summary>
public class CrashRecoveryLogger
{
    private const int CrashThreshold = 3;
    private const string EventLogSource = "wkl";
    private const string EventLogName = "Application";

    private readonly string _crashCountFilePath;

    public CrashRecoveryLogger()
        : this(Path.Combine(AppContext.BaseDirectory, "crash_count.txt"))
    {
    }

    public CrashRecoveryLogger(string crashCountFilePath)
    {
        _crashCountFilePath = crashCountFilePath;
    }

    /// <summary>读取当前崩溃计数</summary>
    public int GetCrashCount()
    {
        try
        {
            if (File.Exists(_crashCountFilePath))
            {
                string content = File.ReadAllText(_crashCountFilePath).Trim();
                if (int.TryParse(content, out int count))
                    return count;
            }
        }
        catch
        {
            // File read failure — treat as 0
        }

        return 0;
    }

    /// <summary>递增崩溃计数并在超过阈值时写入事件日志</summary>
    public void IncrementCrashCount()
    {
        int count = GetCrashCount() + 1;
        WriteCrashCount(count);

        if (ShouldLogToEventLog(count))
        {
            WriteEventLog(count);
        }
    }

    /// <summary>判断是否需要写入事件日志（崩溃次数 > 3）</summary>
    public static bool ShouldLogToEventLog(int crashCount)
    {
        return crashCount > CrashThreshold;
    }

    /// <summary>成功启动后重置崩溃计数</summary>
    public void ResetCrashCount()
    {
        WriteCrashCount(0);
    }

    private void WriteCrashCount(int count)
    {
        try
        {
            File.WriteAllText(_crashCountFilePath, count.ToString());
        }
        catch
        {
            // Best-effort write
        }
    }

    private static void WriteEventLog(int crashCount)
    {
        try
        {
            string rawMessage = $"Service wkl has crashed {crashCount} times consecutively at {DateTime.UtcNow:O}. Automatic restart may be unreliable.";
            string filteredMessage = SensitiveWordFilter.FilterLogMessage(rawMessage);

            if (!EventLog.SourceExists(EventLogSource))
            {
                EventLog.CreateEventSource(EventLogSource, EventLogName);
            }

            EventLog.WriteEntry(EventLogSource, filteredMessage, EventLogEntryType.Warning);
        }
        catch
        {
            // Event log write failure — best effort, don't crash the service
        }
    }
}
