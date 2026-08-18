using System.Text.Json;
using StackExchange.Redis;

namespace MeuCatan.Api.Services;

internal static class RedisStateStoreSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static RedisValue Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, Options);
    }

    public static T? Deserialize<T>(RedisValue value)
    {
        return value.IsNullOrEmpty
            ? default
            : JsonSerializer.Deserialize<T>(value.ToString()!, Options);
    }
}

internal static class RedisStateStoreKeys
{
    public const string LobbyRoomIds = "meucatan:lobby:room-ids";
    public const string LobbyNextRoomId = "meucatan:lobby:next-room-id";
    public const string LobbyWriteLock = "meucatan:locks:lobby-room-store";
    public const string GameWriteLock = "meucatan:locks:game-session-store";
    public const string GameSessionIds = "meucatan:game:session-ids";

    public static string LobbyRoom(int salaId) => $"meucatan:lobby:room:{salaId}";
    public static string GameSession(int salaId) => $"meucatan:game:session:{salaId}";
}

internal sealed class RedisStoreLockHandle : IDisposable
{
    private static readonly LuaScript ReleaseScript = LuaScript.Prepare(
        "if redis.call('get', @key) == @token then return redis.call('del', @key) else return 0 end");

    private readonly IDatabase _database;
    private readonly RedisKey _key;
    private readonly RedisValue _token;
    private bool _disposed;

    private RedisStoreLockHandle(IDatabase database, RedisKey key, RedisValue token)
    {
        _database = database;
        _key = key;
        _token = token;
    }

    public static RedisStoreLockHandle Acquire(IDatabase database, string key)
    {
        var token = Guid.NewGuid().ToString("N");
        var expiresIn = TimeSpan.FromSeconds(10);
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (DateTime.UtcNow < deadline)
        {
            if (database.StringSet(key, token, expiresIn, When.NotExists))
            {
                return new RedisStoreLockHandle(database, key, token);
            }

            Thread.Sleep(50);
        }

        throw new InvalidOperationException($"Não foi possível adquirir o lock distribuído '{key}'.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _database.ScriptEvaluate(ReleaseScript, new { key = _key, token = _token });
        _disposed = true;
    }
}