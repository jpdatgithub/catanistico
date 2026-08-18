namespace MeuCatan.Api.Services;

public interface IGameSessionStore
{
    T Read<T>(Func<GameSessionStoreReadContext, T> query);
    T Write<T>(Func<GameSessionStoreWriteContext, T> mutation);
}

public sealed class InMemoryGameSessionStore : IGameSessionStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<int, CatanGameSessionState> _sessions = [];

    public T Read<T>(Func<GameSessionStoreReadContext, T> query)
    {
        lock (_lock)
        {
            return query(new GameSessionStoreReadContext(salaId =>
                _sessions.TryGetValue(salaId, out var session) ? session : null));
        }
    }

    public T Write<T>(Func<GameSessionStoreWriteContext, T> mutation)
    {
        lock (_lock)
        {
            return mutation(new GameSessionStoreWriteContext(
                salaId => _sessions.TryGetValue(salaId, out var session) ? session : null,
                session => _sessions[session.SalaId] = session,
                salaId => _sessions.Remove(salaId)));
        }
    }
}

public sealed class GameSessionStoreReadContext
{
    private readonly Func<int, CatanGameSessionState?> _getSession;

    internal GameSessionStoreReadContext(Func<int, CatanGameSessionState?> getSession)
    {
        _getSession = getSession;
    }

    public CatanGameSessionState? GetSessionOrDefault(int salaId)
    {
        return _getSession(salaId);
    }
}

public sealed class GameSessionStoreWriteContext
{
    private readonly Func<int, CatanGameSessionState?> _getSession;
    private readonly Action<CatanGameSessionState> _save;
    private readonly Func<int, bool> _remove;

    internal GameSessionStoreWriteContext(
        Func<int, CatanGameSessionState?> getSession,
        Action<CatanGameSessionState> save,
        Func<int, bool> remove)
    {
        _getSession = getSession;
        _save = save;
        _remove = remove;
    }

    public CatanGameSessionState? GetSessionOrDefault(int salaId)
    {
        return _getSession(salaId);
    }

    public void Save(CatanGameSessionState session)
    {
        _save(session);
    }

    public bool Remove(int salaId)
    {
        return _remove(salaId);
    }
}