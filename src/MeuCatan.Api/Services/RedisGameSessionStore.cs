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
        return query(new GameSessionStoreReadContext(GetSessionOrDefault));
    }

    public T Write<T>(Func<GameSessionStoreWriteContext, T> mutation)
    {
        using var lockHandle = RedisStoreLockHandle.Acquire(_database, RedisStateStoreKeys.GameWriteLock);

        return mutation(new GameSessionStoreWriteContext(GetSessionOrDefault, SaveSession));
    }

    private CatanGameSessionState? GetSessionOrDefault(int salaId)
    {
        return RedisStateStoreSerializer.Deserialize<CatanGameSessionState>(
            _database.StringGet(RedisStateStoreKeys.GameSession(salaId)));
    }

    private void SaveSession(CatanGameSessionState session)
    {
        _database.StringSet(RedisStateStoreKeys.GameSession(session.SalaId), RedisStateStoreSerializer.Serialize(session));
    }
}