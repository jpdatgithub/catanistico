using System.Runtime.InteropServices;
using MeuCatan.ClassLib.Contracts;
using static MeuCatan.ClassLib.Utils.HexUtils;

namespace MeuCatan.Api.Services;

public interface IGameSessionService
{
    LobbyOperationResult<GameSessionResponse> CreateGameSessionFromRoom(RoomGameStartContext roomContext);
    LobbyOperationResult<GameSessionResponse> GetSession(int salaId, int usuarioId);
}

public sealed class CatanGameSessionService : IGameSessionService
{
    private static readonly string[] PlayerColors = ["vermelho", "azul", "branco", "laranja"];

    private static readonly (int X, int Y, int Z)[] OrderedOuterCoordinates =
    [
        (-2, 2, 0), (-1, 2, -1), (0, 2, -2), (1, 1, -2), (2, 0, -2), (2, -1, -1), (2, -2, 0), (1, -2, 1), (0, -2, 2), (-1, -1, 2), (-2, 0, 2), (-2, 1, 1)
    ];
    private static readonly (int X, int Y, int Z)[] OrderedMiddleCoordinates =
    [
        (-1, 1, 0), (0, 1, -1), (1, 0, -1), (1, -1, 0), (0, -1, 1), (-1, 0, 1)
    ];

    private static readonly int[] ThreeToFourOrderedNumberTokens =
    [
        5, 2, 6, 3, 8, 10, 9, 12, 11, 4, 8, 10, 9, 4, 5, 6, 3, 11
    ];

    private readonly Lock _lock = new();
    private readonly Dictionary<int, CatanGameSessionState> _sessions = [];

    public LobbyOperationResult<GameSessionResponse> CreateGameSessionFromRoom(RoomGameStartContext roomContext)
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
                Board = Create34TraditionalBoardState()
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

    private static CatanBoardState Create34TraditionalBoardState()
    {
        var board = new CatanBoardState { };

        var recursos = new List<string>();

        for (int i = 0; i < 4; i++)
        {
            recursos.Add("madeira");
            recursos.Add("ovelha");
            recursos.Add("trigo");
            if (i < 3)
            {
                recursos.Add("argila");
                recursos.Add("pedra");
            }
            if (i == 0)
            {
                recursos.Add("deserto");
            }
        }

        Random.Shared.Shuffle(CollectionsMarshal.AsSpan(recursos));

        var indiceDeserto = recursos.IndexOf("deserto");

        var numberTokens = ThreeToFourOrderedNumberTokens.ToList();
        numberTokens.Insert(indiceDeserto, 7);

        var start = Random.Shared.Next(6);

        var OrderedTiles = new List<(int X, int Y, int Z)>();
        var rotatedOuter = OrderedOuterCoordinates.Skip(2 * start).Concat(OrderedOuterCoordinates.Take(2 * start));
        var rotatedInner = OrderedMiddleCoordinates.Skip(start).Concat(OrderedMiddleCoordinates.Take(start));

        OrderedTiles.AddRange(rotatedOuter);
        OrderedTiles.AddRange(rotatedInner);
        OrderedTiles.Add((0, 0, 0));

        var vertices = new Dictionary<(double X, double Y), CatanVertexState>();

        var tiles = OrderedTiles
            .Select((hexagon, index) =>
            {
                var hexCenter = CubeToCenterPixel(board.width / 2.0, board.height / 2.0, hexagon.X, hexagon.Y, hexagon.Z, 100.0);

                CalcularPontosSvg(hexCenter.X, hexCenter.Y, 100.0)
                    .ForEach(point =>
                    {
                        if (!vertices.ContainsKey(point))
                        {
                            var catanVertex = new CatanVertexState
                            {
                                VertexId = vertices.Count + 1,
                                Position = point,
                                Resources = [recursos[index]],
                            };
                            vertices[point] = catanVertex;
                        }
                        else
                        {
                            vertices[point].Resources.Add(recursos[index]);
                        }
                    });

                return new CatanTileState
                {
                    TileId = index + 1,
                    ResourceType = recursos[index],
                    NumberToken = numberTokens[index],
                    CubeX = hexagon.X,
                    CubeY = hexagon.Y,
                    CubeZ = hexagon.Z,
                    CenterX = hexCenter.X,
                    CenterY = hexCenter.Y
                };
            })
            .ToList();

        board.Vertices = vertices.Values.ToList();
        board.Tiles = tiles;

        return board;
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
                width = session.Board.width,
                height = session.Board.height,
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
                        IsAvailableForAction = vertex.OwnerPlayerId is null && canCurrentUserAct,
                        Resources = vertex.Resources,
                        Ports = vertex.Ports,
                        Position = vertex.Position
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
        public int width { get; set; } = 1000;
        public int height { get; set; } = 1000;
    }

    private sealed class CatanTileState
    {
        public int TileId { get; set; }
        public string ResourceType { get; set; } = string.Empty;
        public int NumberToken { get; set; }
        public int CubeX { get; set; }
        public int CubeY { get; set; }
        public int CubeZ { get; set; }
        public double CenterX { get; set; }
        public double CenterY { get; set; }
    }

    private sealed class CatanVertexState
    {
        public int VertexId { get; set; }
        public int? OwnerPlayerId { get; set; }
        public string? BuildingType { get; set; }
        public (double, double) Position { get; set; }
        public List<string> Resources { get; set; } = [];
        public List<string> Ports { get; set; } = [];
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
