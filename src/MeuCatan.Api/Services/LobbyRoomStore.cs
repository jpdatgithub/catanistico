namespace MeuCatan.Api.Services;

public interface ILobbyRoomStore
{
    T Read<T>(Func<LobbyRoomStoreReadContext, T> query);
    T Write<T>(Func<LobbyRoomStoreWriteContext, T> mutation);
}

public sealed class InMemoryLobbyRoomStore : ILobbyRoomStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<int, LobbyRoomState> _rooms = [];
    private int _lastRoomId;

    public T Read<T>(Func<LobbyRoomStoreReadContext, T> query)
    {
        lock (_lock)
        {
            return query(new LobbyRoomStoreReadContext(
                () => _rooms.Values.ToList(),
                salaId => _rooms.GetValueOrDefault(salaId)));
        }
    }

    public T Write<T>(Func<LobbyRoomStoreWriteContext, T> mutation)
    {
        lock (_lock)
        {
            return mutation(new LobbyRoomStoreWriteContext(
                salaId => _rooms.GetValueOrDefault(salaId),
                () => ++_lastRoomId,
                room => _rooms[room.SalaId] = room,
                salaId => _rooms.Remove(salaId)));
        }
    }
}

public sealed class LobbyRoomStoreReadContext
{
    private readonly Func<IReadOnlyCollection<LobbyRoomState>> _getRooms;
    private readonly Func<int, LobbyRoomState?> _getRoom;

    internal LobbyRoomStoreReadContext(Func<IReadOnlyCollection<LobbyRoomState>> getRooms, Func<int, LobbyRoomState?> getRoom)
    {
        _getRooms = getRooms;
        _getRoom = getRoom;
    }

    public IReadOnlyCollection<LobbyRoomState> Rooms => _getRooms();

    public LobbyRoomState? GetRoomOrDefault(int salaId)
    {
        return _getRoom(salaId);
    }
}

public sealed class LobbyRoomStoreWriteContext
{
    private readonly Func<int, LobbyRoomState?> _getRoom;
    private readonly Func<int> _nextRoomId;
    private readonly Action<LobbyRoomState> _save;
    private readonly Func<int, bool> _remove;

    internal LobbyRoomStoreWriteContext(
        Func<int, LobbyRoomState?> getRoom,
        Func<int> nextRoomId,
        Action<LobbyRoomState> save,
        Func<int, bool> remove)
    {
        _getRoom = getRoom;
        _nextRoomId = nextRoomId;
        _save = save;
        _remove = remove;
    }

    public LobbyRoomState? GetRoomOrDefault(int salaId)
    {
        return _getRoom(salaId);
    }

    public int NextRoomId()
    {
        return _nextRoomId();
    }

    public void Save(LobbyRoomState room)
    {
        _save(room);
    }

    public bool Remove(int salaId)
    {
        return _remove(salaId);
    }
}