using MeuCatan.ClassLib.Contracts;

namespace MeuCatan.Api.Services;

public interface IGameSessionService
{
    LobbyOperationResult<GameSessionResponse> CreateCatanSessionFromRoom(RoomGameStartContext roomContext);
    LobbyOperationResult<GameSessionResponse> GetSession(int salaId, int usuarioId);
}

public sealed class GameSessionService : IGameSessionService
{
    private static readonly string[] PlayerColors = ["vermelho", "azul", "branco", "laranja"];
    private static readonly (int X, int Y, int Z)[] DefaultTileCubeCoordinates =
    [
        (0, 2, -2), (1, 1, -2), (2, 0, -2),
        (-1, 2, -1), (0, 1, -1), (1, 0, -1), (2, -1, -1),
        (-2, 2, 0), (-1, 1, 0), (0, 0, 0), (1, -1, 0), (2, -2, 0),
        (-2, 1, 1), (-1, 0, 1), (0, -1, 1), (1, -2, 1),
        (-2, 0, 2), (-1, -1, 2), (0, -2, 2)
    ];

    private readonly Lock _lock = new();
    private readonly Dictionary<int, CatanGameSessionState> _sessions = [];

    public LobbyOperationResult<GameSessionResponse> CreateCatanSessionFromRoom(RoomGameStartContext roomContext)
    {
        if (!string.Equals(roomContext.GameType, LobbyTipoJogo.CatanBase, StringComparison.OrdinalIgnoreCase))
        {
            return LobbyOperationResult<GameSessionResponse>.Validation("Tipo de jogo ainda não suportado para criação de sessão.");
        }

        lock (_lock)
        {
            if (_sessions.TryGetValue(roomContext.SalaId, out var existingSession))
            {
                return LobbyOperationResult<GameSessionResponse>.Ok(ToResponse(existingSession, roomContext.CriadorId));
            }

            var orderedPlayers = roomContext.Players
                .OrderBy(player => player.JoinedAtUtc)
                .ToList();

            var session = new CatanGameSessionState
            {
                SalaId = roomContext.SalaId,
                GameType = roomContext.GameType,
                Phase = GameTipoFase.SetupInicial,
                SetupStepIndex = 0,
                SetupTurnOrder = BuildSetupTurnOrder(orderedPlayers.Select(player => player.UsuarioId).ToList()),
                Players = orderedPlayers
                    .Select((player, index) => new CatanPlayerState
                    {
                        UsuarioId = player.UsuarioId,
                        Nome = player.Nome,
                        Cor = PlayerColors[index % PlayerColors.Length],
                        Pontos = 0,
                        RemainingRoads = 15,
                        RemainingSettlements = 5,
                        RemainingCities = 4,
                        Resources = new Dictionary<string, int>()
                    })
                    .ToList(),
                Board = CreateBoardState()
            };

            session.CurrentPlayerId = session.SetupTurnOrder.First();
            _sessions[roomContext.SalaId] = session;

            return LobbyOperationResult<GameSessionResponse>.Ok(ToResponse(session, roomContext.CriadorId));
        }
    }

    public LobbyOperationResult<GameSessionResponse> GetSession(int salaId, int usuarioId)
    {
        lock (_lock)
        {
            if (!_sessions.TryGetValue(salaId, out var session))
            {
                return LobbyOperationResult<GameSessionResponse>.NotFound("Sessão de jogo não encontrada.");
            }

            if (session.Players.All(player => player.UsuarioId != usuarioId))
            {
                return LobbyOperationResult<GameSessionResponse>.Forbidden("Você não participa desta sessão.");
            }

            return LobbyOperationResult<GameSessionResponse>.Ok(ToResponse(session, usuarioId));
        }
    }

    private static List<int> BuildSetupTurnOrder(List<int> playerIds)
    {
        var reverse = playerIds.Count > 1
            ? playerIds.Take(playerIds.Count - 1).Reverse()
            : [];

        return [.. playerIds, .. reverse];
    }

    private static CatanBoardState CreateBoardState()
    {
        var tileDefinitions = new (string ResourceType, int NumberToken)[]
        {
            ("madeira", 11),
            ("argila", 12),
            ("ovelha", 9),
            ("trigo", 4),
            ("pedra", 6),
            ("madeira", 5),
            ("ovelha", 10),
            ("trigo", 3),
            ("argila", 8),
            ("deserto", 0),
            ("pedra", 8),
            ("madeira", 3),
            ("trigo", 4),
            ("ovelha", 5),
            ("pedra", 6),
            ("madeira", 9),
            ("argila", 10),
            ("trigo", 11),
            ("ovelha", 2)
        };

        return new CatanBoardState
        {
            RobberTileId = 10,
            Tiles = tileDefinitions
                .Select((tile, index) =>
                {
                    var cube = DefaultTileCubeCoordinates[index];
                    return new CatanTileState
                    {
                        TileId = index + 1,
                        ResourceType = tile.ResourceType,
                        NumberToken = tile.NumberToken,
                        CubeX = cube.X,
                        CubeY = cube.Y,
                        CubeZ = cube.Z
                    };
                })
                .ToList(),
            Vertices = Enumerable.Range(1, 54)
                .Select(id => new CatanVertexState
                {
                    VertexId = id
                })
                .ToList(),
            Edges = Enumerable.Range(1, 72)
                .Select(id => new CatanEdgeState
                {
                    EdgeId = id,
                    VertexAId = ((id - 1) % 54) + 1,
                    VertexBId = (id % 54) + 1
                })
                .ToList()
        };
    }

    private static GameSessionResponse ToResponse(CatanGameSessionState session, int usuarioId)
    {
        var currentPlayer = session.Players.First(player => player.UsuarioId == session.CurrentPlayerId);
        var canCurrentUserAct = currentPlayer.UsuarioId == usuarioId;

        return new GameSessionResponse
        {
            SalaId = session.SalaId,
            GameType = session.GameType,
            Phase = session.Phase,
            CurrentPlayerId = currentPlayer.UsuarioId,
            CurrentPlayerNome = currentPlayer.Nome,
            YourPlayerId = usuarioId,
            CanCurrentUserAct = canCurrentUserAct,
            AvailableActions = canCurrentUserAct
                ? [GameActionTypes.PlaceInitialSettlement]
                : [],
            Players = session.Players
                .Select(player => new GamePlayerStateResponse
                {
                    UsuarioId = player.UsuarioId,
                    Nome = player.Nome,
                    Cor = player.Cor,
                    Pontos = player.Pontos,
                    IsCurrentTurn = player.UsuarioId == session.CurrentPlayerId,
                    RemainingRoads = player.RemainingRoads,
                    RemainingSettlements = player.RemainingSettlements,
                    RemainingCities = player.RemainingCities,
                    Resources = new Dictionary<string, int>(player.Resources)
                })
                .ToList(),
            CatanState = new CatanGameStateResponse
            {
                SetupStepIndex = session.SetupStepIndex,
                SetupTurnOrder = [.. session.SetupTurnOrder],
                LastPlacedSettlementVertexId = session.LastPlacedSettlementVertexId,
                RobberTileId = session.Board.RobberTileId,
                Tiles = session.Board.Tiles
                    .Select(tile => new CatanTileResponse
                    {
                        TileId = tile.TileId,
                        ResourceType = tile.ResourceType,
                        NumberToken = tile.NumberToken,
                        CubeX = tile.CubeX,
                        CubeY = tile.CubeY,
                        CubeZ = tile.CubeZ
                    })
                    .ToList(),
                Vertices = session.Board.Vertices
                    .Select(vertex => new CatanVertexResponse
                    {
                        VertexId = vertex.VertexId,
                        OwnerPlayerId = vertex.OwnerPlayerId,
                        BuildingType = vertex.BuildingType,
                        IsAvailableForAction = vertex.OwnerPlayerId is null && canCurrentUserAct
                    })
                    .ToList(),
                Edges = session.Board.Edges
                    .Select(edge => new CatanEdgeResponse
                    {
                        EdgeId = edge.EdgeId,
                        VertexAId = edge.VertexAId,
                        VertexBId = edge.VertexBId,
                        OwnerPlayerId = edge.OwnerPlayerId,
                        IsAvailableForAction = false
                    })
                    .ToList()
            }
        };
    }

    private sealed class CatanGameSessionState
    {
        public int SalaId { get; set; }
        public string GameType { get; set; } = LobbyTipoJogo.CatanBase;
        public GameTipoFase Phase { get; set; } = GameTipoFase.SetupInicial;
        public int CurrentPlayerId { get; set; }
        public int SetupStepIndex { get; set; }
        public List<int> SetupTurnOrder { get; set; } = [];
        public int? LastPlacedSettlementVertexId { get; set; }
        public List<CatanPlayerState> Players { get; set; } = [];
        public CatanBoardState Board { get; set; } = new();
    }

    private sealed class CatanPlayerState
    {
        public int UsuarioId { get; set; }
        public string Nome { get; set; } = string.Empty;
        public string Cor { get; set; } = string.Empty;
        public int Pontos { get; set; }
        public int RemainingRoads { get; set; }
        public int RemainingSettlements { get; set; }
        public int RemainingCities { get; set; }
        public Dictionary<string, int> Resources { get; set; } = [];
    }

    private sealed class CatanBoardState
    {
        public int? RobberTileId { get; set; }
        public List<CatanTileState> Tiles { get; set; } = [];
        public List<CatanVertexState> Vertices { get; set; } = [];
        public List<CatanEdgeState> Edges { get; set; } = [];
    }

    private sealed class CatanTileState
    {
        public int TileId { get; set; }
        public string ResourceType { get; set; } = string.Empty;
        public int NumberToken { get; set; }
        public int CubeX { get; set; }
        public int CubeY { get; set; }
        public int CubeZ { get; set; }
    }

    private sealed class CatanVertexState
    {
        public int VertexId { get; set; }
        public int? OwnerPlayerId { get; set; }
        public string? BuildingType { get; set; }
    }

    private sealed class CatanEdgeState
    {
        public int EdgeId { get; set; }
        public int VertexAId { get; set; }
        public int VertexBId { get; set; }
        public int? OwnerPlayerId { get; set; }
    }
}

public sealed class RoomGameStartContext
{
    public int SalaId { get; set; }
    public string GameType { get; set; } = LobbyTipoJogo.CatanBase;
    public int CriadorId { get; set; }
    public List<RoomGameStartPlayer> Players { get; set; } = [];
}

public sealed class RoomGameStartPlayer
{
    public int UsuarioId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public DateTime JoinedAtUtc { get; set; }
}
