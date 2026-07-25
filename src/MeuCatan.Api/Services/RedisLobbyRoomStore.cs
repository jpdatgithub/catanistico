using StackExchange.Redis;

namespace MeuCatan.Api.Services;

public sealed class RedisLobbyRoomStore : ILobbyRoomStore
{
    private readonly IDatabase _database;

    public RedisLobbyRoomStore(IConnectionMultiplexer connectionMultiplexer)
    {
        _database = connectionMultiplexer.GetDatabase();
    }

    public T Read<T>(Func<LobbyRoomStoreReadContext, T> query)
    {
        return query(new LobbyRoomStoreReadContext(ListRooms, GetRoomOrDefault));
    }

    public T Write<T>(Func<LobbyRoomStoreWriteContext, T> mutation)
    {
        using var lockHandle = RedisStoreLockHandle.Acquire(_database, RedisStateStoreKeys.LobbyWriteLock);

        return mutation(new LobbyRoomStoreWriteContext(
            ListRooms,
            GetRoomOrDefault,
            NextRoomId,
            SaveRoom,
            RemoveRoom));
    }

    private IReadOnlyCollection<LobbyRoomState> ListRooms()
    {
        var roomIds = _database.SetMembers(RedisStateStoreKeys.LobbyRoomIds);
        var rooms = new List<LobbyRoomState>(roomIds.Length);

        foreach (var roomId in roomIds)
        {
            if (!int.TryParse(roomId.ToString(), out var salaId))
            {
                continue;
            }

            var room = GetRoomOrDefault(salaId);
            if (room is not null)
            {
                rooms.Add(room);
            }
        }

        return rooms;
    }

    private LobbyRoomState? GetRoomOrDefault(int salaId)
    {
        return RedisStateStoreSerializer.Deserialize<LobbyRoomState>(
            _database.StringGet(RedisStateStoreKeys.LobbyRoom(salaId)));
    }

    private int NextRoomId()
    {
        return (int)_database.StringIncrement(RedisStateStoreKeys.LobbyNextRoomId);
    }

    private void SaveRoom(LobbyRoomState room)
    {
        _database.StringSet(RedisStateStoreKeys.LobbyRoom(room.SalaId), RedisStateStoreSerializer.Serialize(room));
        _database.SetAdd(RedisStateStoreKeys.LobbyRoomIds, room.SalaId);
    }

    private bool RemoveRoom(int salaId)
    {
        var deleted = _database.KeyDelete(RedisStateStoreKeys.LobbyRoom(salaId));
        _database.SetRemove(RedisStateStoreKeys.LobbyRoomIds, salaId);
        return deleted;
    }
}