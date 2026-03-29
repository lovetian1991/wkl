using LanRemoteControl.Shared;

namespace LanRemoteControl.Agent;

/// <summary>
/// 会话管理器实现。线程安全，同一时间仅允许一个活跃会话（需求 7.5）。
/// </summary>
public class SessionManager : ISessionManager
{
    private readonly object _lock = new();
    private SessionContext? _activeSession;

    public SessionContext? ActiveSession
    {
        get
        {
            lock (_lock)
            {
                return _activeSession;
            }
        }
    }

    public bool TryAcceptSession(SessionContext session)
    {
        lock (_lock)
        {
            if (_activeSession is not null)
                return false;

            session.IsActive = true;
            _activeSession = session;
            return true;
        }
    }

    public void EndSession(string sessionId)
    {
        lock (_lock)
        {
            if (_activeSession is null || _activeSession.SessionId != sessionId)
                return;

            var session = _activeSession;
            _activeSession = null;
            session.IsActive = false;

            // Clean up network resources
            try { session.Stream.Dispose(); } catch { }
            try { session.TcpClient.Dispose(); } catch { }
        }
    }
}
