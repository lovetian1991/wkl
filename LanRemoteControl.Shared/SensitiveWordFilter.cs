namespace LanRemoteControl.Shared;

/// <summary>
/// 敏感词过滤工具类。
/// 用于检测进程/服务名称中的敏感关键词，以及过滤日志消息中的敏感短语。
/// </summary>
public static class SensitiveWordFilter
{
    private static readonly string[] SensitiveProcessKeywords =
    {
        "remote", "control", "spy", "monitor", "capture", "keylog"
    };

    private static readonly string[] SensitiveLogPhrases =
    {
        "remote control", "desktop capture", "screen spy", "input simulation"
    };

    /// <summary>
    /// 检查进程/服务名称是否包含敏感关键词（不区分大小写）。
    /// </summary>
    /// <param name="name">进程或服务名称</param>
    /// <returns>如果包含敏感关键词则返回 true</returns>
    public static bool ContainsSensitiveProcessName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        foreach (var keyword in SensitiveProcessKeywords)
        {
            if (name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 过滤日志消息中的敏感短语，将其替换为 "***"（不区分大小写）。
    /// </summary>
    /// <param name="message">原始日志消息</param>
    /// <returns>过滤后的日志消息</returns>
    public static string FilterLogMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
            return message ?? string.Empty;

        string filtered = message;

        foreach (var phrase in SensitiveLogPhrases)
        {
            // Case-insensitive replacement
            int index;
            while ((index = filtered.IndexOf(phrase, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                filtered = string.Concat(
                    filtered.AsSpan(0, index),
                    "***",
                    filtered.AsSpan(index + phrase.Length));
            }
        }

        return filtered;
    }
}
