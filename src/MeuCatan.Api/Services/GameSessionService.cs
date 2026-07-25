using System.Runtime.InteropServices;
using MeuCatan.ClassLib.Contracts;
using static MeuCatan.ClassLib.Utils.HexUtils;

namespace MeuCatan.Api.Services;

public interface IGameSessionService
{
    LobbyOperationResult<GameSessionResponse> CreateGameSessionFromRoom(RoomGameStartContext roomContext);
    LobbyOperationResult<GameSessionResponse> GetSession(int salaId, int usuarioId);
    LobbyOperationResult<GameSessionResponse> ExecuteAction(int salaId, int usuarioId, GameActionRequest request);
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

    private readonly IGameSessionStore _sessionStore;
    private readonly IGameStateEventPublisher _gameStateEventPublisher;

    public CatanGameSessionService(IGameSessionStore sessionStore, IGameStateEventPublisher gameStateEventPublisher)
    {
        _sessionStore = sessionStore;
        _gameStateEventPublisher = gameStateEventPublisher;
    }

    public LobbyOperationResult<GameSessionResponse> CreateGameSessionFromRoom(RoomGameStartContext roomContext)
    {
        if (!string.Equals(roomContext.GameType, LobbyTipoJogo.CatanBase, StringComparison.OrdinalIgnoreCase))
        {
            return LobbyOperationResult<GameSessionResponse>.Validation("Tipo de jogo ainda não suportado para criação de sessão.");
        }

        return _sessionStore.Write(store =>
        {
            var existingSession = store.GetSessionOrDefault(roomContext.SalaId);
            if (existingSession is not null)
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
            store.Save(session);
            _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(session.SalaId, "session-created");

            return LobbyOperationResult<GameSessionResponse>.Ok(ToResponse(session, roomContext.CriadorId));
        });
    }

    public LobbyOperationResult<GameSessionResponse> GetSession(int salaId, int usuarioId)
    {
        return _sessionStore.Read(store =>
        {
            var session = store.GetSessionOrDefault(salaId);
            if (session is null)
            {
                return LobbyOperationResult<GameSessionResponse>.NotFound("Sessão de jogo não encontrada.");
            }

            if (session.Players.All(player => player.UsuarioId != usuarioId))
            {
                return LobbyOperationResult<GameSessionResponse>.Forbidden("Você não participa desta sessão.");
            }

            return LobbyOperationResult<GameSessionResponse>.Ok(ToResponse(session, usuarioId));
        });
    }

    public LobbyOperationResult<GameSessionResponse> ExecuteAction(int salaId, int usuarioId, GameActionRequest request)
    {
        if (request is null)
        {
            return LobbyOperationResult<GameSessionResponse>.Validation("Ação inválida.");
        }

        return _sessionStore.Write(store =>
        {
            var session = store.GetSessionOrDefault(salaId);
            if (session is null)
            {
                return LobbyOperationResult<GameSessionResponse>.NotFound("Sessão de jogo não encontrada.");
            }

            var actingPlayer = session.Players.FirstOrDefault(player => player.UsuarioId == usuarioId);
            if (actingPlayer is null)
            {
                return LobbyOperationResult<GameSessionResponse>.Forbidden("Você não participa desta sessão.");
            }

            if (session.Phase != GameTipoFase.SetupInicial)
            {
                return LobbyOperationResult<GameSessionResponse>.Validation("A fase atual não permite posicionamento inicial.");
            }

            if (session.CurrentPlayerId != usuarioId)
            {
                return LobbyOperationResult<GameSessionResponse>.Forbidden("Você não pode agir agora.");
            }

            if (string.Equals(request.ActionType, GameActionTypes.PlaceInitialSettlement, StringComparison.OrdinalIgnoreCase))
            {
                var placeSettlementResult = TryPlaceInitialSettlement(session, actingPlayer, usuarioId, request.VertexId);
                if (placeSettlementResult is not null)
                {
                    return placeSettlementResult;
                }
            }
            else if (string.Equals(request.ActionType, GameActionTypes.PlaceInitialRoad, StringComparison.OrdinalIgnoreCase))
            {
                var placeRoadResult = TryPlaceInitialRoad(session, actingPlayer, usuarioId, request.SmallerVertexId, request.BiggerVertexId);
                if (placeRoadResult is not null)
                {
                    return placeRoadResult;
                }
            }
            else
            {
                return LobbyOperationResult<GameSessionResponse>.Validation("Tipo de ação não suportado.");
            }

            store.Save(session);
            _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(session.SalaId, "action-executed");

            return LobbyOperationResult<GameSessionResponse>.Ok(ToResponse(session, usuarioId));
        });
    }

    private static LobbyOperationResult<GameSessionResponse>? TryPlaceInitialSettlement(
        CatanGameSessionState session,
        CatanPlayerState actingPlayer,
        int usuarioId,
        int? vertexId)
    {
        if (session.AwaitingInitialRoadPlacement)
        {
            return LobbyOperationResult<GameSessionResponse>.Validation("Você precisa posicionar a estrada inicial antes de colocar outra aldeia.");
        }

        if (vertexId is null)
        {
            return LobbyOperationResult<GameSessionResponse>.Validation("Informe um vértice para posicionar a aldeia.");
        }

        var vertex = session.Board.Vertices.FirstOrDefault(item => item.VertexId == vertexId.Value);
        if (vertex is null || vertex.VertexId == 0)
        {
            return LobbyOperationResult<GameSessionResponse>.Validation("Vértice inválido.");
        }

        if (vertex.OwnerPlayerId is not null)
        {
            return LobbyOperationResult<GameSessionResponse>.Validation("Esse vértice já possui construção.");
        }

        if (HasAdjacentSettlement(session, vertex.VertexId))
        {
            return LobbyOperationResult<GameSessionResponse>.Validation("Não é possível posicionar uma aldeia ao lado de outra já construída.");
        }

        vertex.OwnerPlayerId = usuarioId;
        vertex.BuildingType = "aldeia";
        actingPlayer.Pontos += 1;
        actingPlayer.RemainingSettlements = Math.Max(0, actingPlayer.RemainingSettlements - 1);

        session.LastPlacedSettlementVertexId = vertex.VertexId;
        session.AwaitingInitialRoadPlacement = true;
        session.PendingInitialRoadFromVertexId = vertex.VertexId;

        return null;
    }

    private static LobbyOperationResult<GameSessionResponse>? TryPlaceInitialRoad(
        CatanGameSessionState session,
        CatanPlayerState actingPlayer,
        int usuarioId,
        int? smallerVertexId,
        int? biggerVertexId)
    {
        if (!session.AwaitingInitialRoadPlacement || session.PendingInitialRoadFromVertexId is null)
        {
            return LobbyOperationResult<GameSessionResponse>.Validation("Você precisa posicionar uma aldeia antes da estrada inicial.");
        }

        if (smallerVertexId is null || biggerVertexId is null)
        {
            return LobbyOperationResult<GameSessionResponse>.Validation("Informe os vértices da aresta para posicionar a estrada.");
        }

        var edgeKey = new EdgeKey(smallerVertexId.Value, biggerVertexId.Value);
        var edge = session.Board.Edges.FirstOrDefault(item =>
            item.EdgeKey.smallerVertexId == edgeKey.smallerVertexId &&
            item.EdgeKey.biggerVertexId == edgeKey.biggerVertexId);

        if (edge is null)
        {
            return LobbyOperationResult<GameSessionResponse>.Validation("Aresta inválida.");
        }

        if (edge.OwnerPlayerId is not null)
        {
            return LobbyOperationResult<GameSessionResponse>.Validation("Essa aresta já possui estrada.");
        }

        var settlementVertexId = session.PendingInitialRoadFromVertexId.Value;
        var isAdjacentToPlacedSettlement = edge.EdgeKey.smallerVertexId == settlementVertexId || edge.EdgeKey.biggerVertexId == settlementVertexId;
        if (!isAdjacentToPlacedSettlement)
        {
            return LobbyOperationResult<GameSessionResponse>.Validation("A estrada inicial deve ser adjacente à aldeia recém posicionada.");
        }

        edge.OwnerPlayerId = usuarioId;
        actingPlayer.RemainingRoads = Math.Max(0, actingPlayer.RemainingRoads - 1);

        session.AwaitingInitialRoadPlacement = false;
        session.PendingInitialRoadFromVertexId = null;
        session.SetupStepIndex += 1;

        if (session.SetupStepIndex >= session.SetupTurnOrder.Count)
        {
            session.Phase = GameTipoFase.Turno;
            session.CurrentPlayerId = session.SetupTurnOrder.First();
        }
        else
        {
            session.CurrentPlayerId = session.SetupTurnOrder[session.SetupStepIndex];
        }

        return null;
    }

    private static List<int> BuildSetupTurnOrder(List<int> playerIds)
    {
        var reverse = playerIds.Count > 1
            ? playerIds.AsEnumerable().Reverse().ToList()
            : [];

        return [.. playerIds, .. reverse];
    }

    private static bool HasAdjacentSettlement(CatanGameSessionState session, int vertexId)
    {
        var adjacentVertexIds = session.Board.Edges
            .Where(edge => edge.EdgeKey.smallerVertexId == vertexId || edge.EdgeKey.biggerVertexId == vertexId)
            .Select(edge => edge.EdgeKey.smallerVertexId == vertexId
                ? edge.EdgeKey.biggerVertexId
                : edge.EdgeKey.smallerVertexId)
            .Distinct();

        return adjacentVertexIds.Any(adjacentVertexId =>
        {
            var adjacentVertex = session.Board.Vertices.FirstOrDefault(vertex => vertex.VertexId == adjacentVertexId);
            return adjacentVertex is not null && adjacentVertex.OwnerPlayerId is not null;
        });
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

        var vertices = new Dictionary<Point, CatanVertexState>();
        var edges = new Dictionary<EdgeKey, CatanEdgeState>();

        var tiles = OrderedTiles
            .Select((hexagon, index) =>
            {
                var hexCenter = CubeToCenterPixel(board.width / 2.0, board.height / 2.0, hexagon.X, hexagon.Y, hexagon.Z, 100.0);

                var previousVertex = new CatanVertexState();
                var firstVertex = new CatanVertexState();

                var points = CalcularPontosSvg(hexCenter.X, hexCenter.Y, 100.0);
                for (int i = 0; i < points.Count; i++)
                {
                    var point = points[i];

                    var catanVertex = new CatanVertexState
                    {
                        Position = new Point(point.X, point.Y),
                    };

                    if (!vertices.ContainsKey(point))
                    {
                        catanVertex.VertexId = vertices.Count + 1;
                        vertices[point] = catanVertex;
                    }
                    else
                    {
                        catanVertex.VertexId = vertices[point].VertexId;
                    }

                    vertices[point].Resources.Add(recursos[index]);

                    if (i != 0)
                    {
                        var edgeKey = new EdgeKey(previousVertex.VertexId, catanVertex.VertexId);
                        if (!edges.ContainsKey(edgeKey))
                        {
                            edges[edgeKey] = new CatanEdgeState
                            {
                                EdgeKey = edgeKey,
                                PointA = previousVertex.Position,
                                PointB = catanVertex.Position
                            };
                        }

                        if (i == points.Count - 1)
                        {
                            edgeKey = new EdgeKey(catanVertex.VertexId, firstVertex.VertexId);
                            if (!edges.ContainsKey(edgeKey))
                            {
                                edges[edgeKey] = new CatanEdgeState
                                {
                                    EdgeKey = edgeKey,
                                    PointA = catanVertex.Position,
                                    PointB = firstVertex.Position
                                };
                            }
                        }
                    }
                    else
                    {
                        firstVertex = catanVertex;
                    }
                    previousVertex = catanVertex;
                }

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
        board.Edges = edges.Values.ToList();

        return board;
    }

    private static GameSessionResponse ToResponse(CatanGameSessionState session, int usuarioId)
    {
        var currentPlayer = session.Players.First(player => player.UsuarioId == session.CurrentPlayerId);
        var canCurrentUserAct = currentPlayer.UsuarioId == usuarioId;
        var isSetupPhase = session.Phase == GameTipoFase.SetupInicial;
        var canPlaceInitialSettlement = isSetupPhase && canCurrentUserAct && !session.AwaitingInitialRoadPlacement;
        var canPlaceInitialRoad = isSetupPhase && canCurrentUserAct && session.AwaitingInitialRoadPlacement && session.PendingInitialRoadFromVertexId is not null;
        var pendingRoadVertexId = session.PendingInitialRoadFromVertexId;

        var result = new GameSessionResponse
        {
            SalaId = session.SalaId,
            GameType = session.GameType,
            Phase = session.Phase,
            CurrentPlayerId = currentPlayer.UsuarioId,
            CurrentPlayerNome = currentPlayer.Nome,
            YourPlayerId = usuarioId,
            CanCurrentUserAct = canCurrentUserAct,
            AvailableActions = canPlaceInitialSettlement
                ? [GameActionTypes.PlaceInitialSettlement]
                : canPlaceInitialRoad
                    ? [GameActionTypes.PlaceInitialRoad]
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
                AwaitingInitialRoadPlacement = session.AwaitingInitialRoadPlacement,
                PendingInitialRoadFromVertexId = pendingRoadVertexId,
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
                        IsAvailableForAction = vertex.OwnerPlayerId is null && canPlaceInitialSettlement,
                        Resources = vertex.Resources,
                        Ports = vertex.Ports,
                        Position = vertex.Position
                    })
                    .ToList(),
                Edges = session.Board.Edges
                    .Select(edge => new CatanEdgeResponse
                    {
                        smallerVertexId = edge.EdgeKey.smallerVertexId,
                        biggerVertexId = edge.EdgeKey.biggerVertexId,
                        OwnerPlayerId = edge.OwnerPlayerId,
                        IsAvailableForAction = canPlaceInitialRoad &&
                            edge.OwnerPlayerId is null &&
                            pendingRoadVertexId is not null &&
                            (edge.EdgeKey.smallerVertexId == pendingRoadVertexId.Value || edge.EdgeKey.biggerVertexId == pendingRoadVertexId.Value),
                        PointA = edge.PointA,
                        PointB = edge.PointB
                    })
                    .ToList()
            }
        };

        return result;
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
