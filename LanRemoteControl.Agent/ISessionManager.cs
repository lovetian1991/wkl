using LanRemoteControl.Shared;

namespace LanRemoteControl.Agent;

/// <summary>管理活跃会话，确保同一时间仅一个会话</summary>
public interface ISessionManager
{
    /// <summary>当前活跃会话，无活跃会话时为 null</summary>
    SessionContext? ActiveSession { get; }

    /// <summary>尝试接受新会话。仅当无活跃会话时返回 true</summary>
    bool TryAcceptSession(SessionContext session);

    /// <summary>结束指定会话并清理资源</summary>
    void EndSession(string sessionId);
}
