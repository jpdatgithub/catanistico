using StackExchange.Redis;

namespace MeuCatan.Api.Services;

public sealed class RedisGameSessionStore : IGameSessionStore
{
    private readonly IDatabase _database;

    public RedisGameSessionStore(IConnectionMultiplexer connectionMultiplexer)
    {
        _database = connectionMultiplexer.GetDatabase();
    }

    public T Read<T>(Func<GameSessionStoreReadContext, T> query)
    {
        return query(new GameSessionStoreReadContext(GetSessionOrDefault, GetSessionIds));
    }

    public T Write<T>(Func<GameSessionStoreWriteContext, T> mutation)
    {
        using var lockHandle = RedisStoreLockHandle.Acquire(_database, RedisStateStoreKeys.GameWriteLock);

        return mutation(new GameSessionStoreWriteContext(GetSessionOrDefault, GetSessionIds, SaveSession, RemoveSession));
    }

    private IReadOnlyCollection<int> GetSessionIds()
    {
        return _database.SetMembers(RedisStateStoreKeys.GameSessionIds)
            .Select(value => int.TryParse(value.ToString(), out var salaId) ? salaId : 0)
            .Where(salaId => salaId > 0)
            .ToList();
    }

    private CatanGameSessionState? GetSessionOrDefault(int salaId)
    {
        return RedisStateStoreSerializer.Deserialize<CatanGameSessionState>(
            _database.StringGet(RedisStateStoreKeys.GameSession(salaId)));
    }

    private void SaveSession(CatanGameSessionState session)
    {
        _database.StringSet(RedisStateStoreKeys.GameSession(session.SalaId), RedisStateStoreSerializer.Serialize(session));
        _database.SetAdd(RedisStateStoreKeys.GameSessionIds, session.SalaId);
    }

    private bool RemoveSession(int salaId)
    {
        _database.SetRemove(RedisStateStoreKeys.GameSessionIds, salaId);
        return _database.KeyDelete(RedisStateStoreKeys.GameSession(salaId));
    }
}