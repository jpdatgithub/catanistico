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
    private static readonly string[] PortTypePool =
    [
        "generic_3to1", "generic_3to1", "generic_3to1", "generic_3to1",
        "wood_2to1", "brick_2to1", "sheep_2to1", "wheat_2to1", "ore_2to1"
    ];
    private const int FixedPortSlotCount = 9;
    private const string SettlementBuildingType = "aldeia";
    private const string CityBuildingType = "cidade";
    private const double HexRadius = 100.0;
    private const double VertexAdjacencyTolerance = 1.0;
    private static readonly TimeSpan TradeOfferLifetime = TimeSpan.FromSeconds(10);
    private const int DefaultBankTradeRate = 4;
    private const int GenericHarborTradeRate = 3;
    private const int SpecificHarborTradeRate = 2;

    private static readonly string[] TradableResources = ["madeira", "argila", "ovelha", "trigo", "pedra"];
    private static readonly (string ResourceType, int Amount)[] DevelopmentCardCost =
    [
        ("ovelha", 1),
        ("trigo", 1),
        ("pedra", 1)
    ];

    private static readonly (string ResourceType, int Amount)[] RoadCost =
    [
        ("madeira", 1),
        ("argila", 1)
    ];

    private static readonly (string ResourceType, int Amount)[] VillageCost =
    [
        ("madeira", 1),
        ("argila", 1),
        ("ovelha", 1),
        ("trigo", 1)
    ];

    private static readonly (string ResourceType, int Amount)[] CityCost =
    [
        ("trigo", 2),
        ("pedra", 3)
    ];

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
    private const string DesertResourceType = "deserto";

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
                EnsureBankInitialized(existingSession);
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
                        Resources = new Dictionary<string, int>(),
                        DevelopmentCards = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                    })
                    .ToList(),
                Bank = CatanBankState.CreateDefault(),
                Board = Create34TraditionalBoardState()
            };

            InitializeDevelopmentDeck(session.Bank);
            session.CurrentPlayerId = session.SetupTurnOrder.First();
            store.Save(session);
            _ = _gameStateEventPublisher.PublishGameStateInvalidationAsync(session.SalaId, "session-created");

            return LobbyOperationResult<GameSessionResponse>.Ok(ToResponse(session, roomContext.CriadorId));
        });
    }

    public LobbyOperationResult<GameSessionResponse> GetSession(int salaId, int usuarioId)
    {
        return _sessionStore.Write(store =>
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

            EnsureBankInitialized(session);
            EnsurePendingDiscardStateInitialized(session);

            if (RemoveExpiredTradeOffers(session, DateTime.UtcNow))
            {
                store.Save(session);
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

            EnsureBankInitialized(session);
            EnsurePendingDiscardStateInitialized(session);

            RemoveExpiredTradeOffers(session, DateTime.UtcNow);

            var isDiscardAction = string.Equals(request.ActionType, GameActionTypes.DiscardResources, StringComparison.OrdinalIgnoreCase);

            if (!isDiscardAction && HasAnyPendingDiscards(session))
            {
                return LobbyOperationResult<GameSessionResponse>.Validation("Existe descarte pendente após o resultado 7. Conclua os descartes para continuar o turno.");
            }

            if (session.CurrentPlayerId != usuarioId && !isDiscardAction)
            {
                return LobbyOperationResult<GameSessionResponse>.Forbidden("Você não pode agir agora.");
            }

            if (string.Equals(request.ActionType, GameActionTypes.PlaceInitialSettlement, StringComparison.OrdinalIgnoreCase))
            {
                if (session.Phase != GameTipoFase.SetupInicial)
                {
                    return LobbyOperationResult<GameSessionResponse>.Validation("A fase atual não permite posicionamento inicial.");
                }

                var placeSettlementResult = TryPlaceInitialSettlement(session, actingPlayer, usuarioId, request.VertexId);
                if (placeSettlementResult is not null)
                {
                    return placeSettlementResult;
                }
            }
            else if (string.Equals(request.ActionType, GameActionTypes.PlaceInitialRoad, StringComparison.OrdinalIgnoreCase))
            {
                if (session.Phase != GameTipoFase.SetupInicial)
                {
                    return LobbyOperationResult<GameSessionResponse>.Validation("A fase atual não permite posicionamento inicial.");
                }

                var placeRoadResult = TryPlaceInitialRoad(session, actingPlayer, usuarioId, request.SmallerVertexId, request.BiggerVertexId);
                if (placeRoadResult is not null)
                {
                    return placeRoadResult;
                }
            }
            else if (string.Equals(request.ActionType, GameActionTypes.RollDice, StringComparison.OrdinalIgnoreCase))
            {
                if (session.Phase != GameTipoFase.Turno)
                {
                    return LobbyOperationResult<GameSessionResponse>.Validation("A fase atual não permite rolar os dados.");
                }

                var rollDiceResult = TryRollDice(session);
                if (rollDiceResult is not null)
                {
                    return rollDiceResult;
                }
            }
            else if (string.Equals(request.ActionType, GameActionTypes.EndTurn, StringComparison.OrdinalIgnoreCase))
            {
                if (session.Phase != GameTipoFase.Turno)
                {
                    return LobbyOperationResult<GameSessionResponse>.Validation("A fase atual não permite passar o turno.");
                }

                var endTurnResult = TryEndTurn(session);
                if (endTurnResult is not null)
                {
                    return endTurnResult;
                }
            }
            else if (string.Equals(request.ActionType, GameActionTypes.OfferTrade, StringComparison.OrdinalIgnoreCase))
            {
                if (session.Phase != GameTipoFase.Turno || !session.HasRolledDiceThisTurn)
                {
                    return LobbyOperationResult<GameSessionResponse>.Validation("Você só pode oferecer trocas após rolar os dados.");
                }

                var offerTradeResult = TryOfferTrade(session, actingPlayer, request);
                if (offerTradeResult is not null)
                {
                    return offerTradeResult;
                }
            }
            else if (string.Equals(request.ActionType, GameActionTypes.TradeWithBank, StringComparison.OrdinalIgnoreCase))
            {
                if (session.Phase != GameTipoFase.Turno || !session.HasRolledDiceThisTurn)
                {
                    return LobbyOperationResult<GameSessionResponse>.Validation("Você só pode trocar com o banco após rolar os dados.");
                }

                var bankTradeResult = TryTradeWithBank(session, actingPlayer, request);
                if (bankTradeResult is not null)
                {
                    return bankTradeResult;
                }
            }
            else if (string.Equals(request.ActionType, GameActionTypes.BuyDevelopmentCard, StringComparison.OrdinalIgnoreCase))
            {
                if (session.Phase != GameTipoFase.Turno || !session.HasRolledDiceThisTurn)
                {
                    return LobbyOperationResult<GameSessionResponse>.Validation("Você só pode comprar uma carta de desenvolvimento após rolar os dados.");
                }

                var buyDevelopmentCardResult = TryBuyDevelopmentCard(session, actingPlayer);
                if (buyDevelopmentCardResult is not null)
                {
                    return buyDevelopmentCardResult;
                }
            }
            else if (string.Equals(request.ActionType, GameActionTypes.BuildRoad, StringComparison.OrdinalIgnoreCase))
            {
                if (session.Phase != GameTipoFase.Turno || !session.HasRolledDiceThisTurn)
                {
                    return LobbyOperationResult<GameSessionResponse>.Validation("Você só pode construir uma estrada após rolar os dados.");
                }

                var buildRoadResult = TryBuildRoad(session, actingPlayer, request.SmallerVertexId, request.BiggerVertexId);
                if (buildRoadResult is not null)
                {
                    return buildRoadResult;
                }
            }
            else if (string.Equals(request.ActionType, GameActionTypes.BuildVillage, StringComparison.OrdinalIgnoreCase))
            {
                if (session.Phase != GameTipoFase.Turno || !session.HasRolledDiceThisTurn)
                {
                    return LobbyOperationResult<GameSessionResponse>.Validation("Você só pode construir um vilarejo após rolar os dados.");
                }

                var buildVillageResult = TryBuildVillage(session, actingPlayer, request.VertexId);
                if (buildVillageResult is not null)
                {
                    return buildVillageResult;
                }
            }
            else if (string.Equals(request.ActionType, GameActionTypes.BuildCity, StringComparison.OrdinalIgnoreCase))
            {
                if (session.Phase != GameTipoFase.Turno || !session.HasRolledDiceThisTurn)
                {
                    return LobbyOperationResult<GameSessionResponse>.Validation("Você só pode construir uma cidade após rolar os dados.");
                }

                var buildCityResult = TryBuildCity(session, actingPlayer, request.VertexId);
                if (buildCityResult is not null)
                {
                    return buildCityResult;
                }
            }
            else if (isDiscardAction)
            {
                if (session.Phase != GameTipoFase.Turno || !session.HasRolledDiceThisTurn)
                {
                    return LobbyOperationResult<GameSessionResponse>.Validation("Você só pode descartar durante o turno após rolar os dados.");
                }

                var discardResult = TryDiscardResources(session, actingPlayer, request.OfferedResources);
                if (discardResult is not null)
                {
                    return discardResult;
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
        var validationError = ValidateInitialSettlementPlacement(session, vertexId);
        if (validationError is not null)
        {
            return LobbyOperationResult<GameSessionResponse>.Validation(validationError);
        }

        var vertex = session.Board.Vertices.First(item => item.VertexId == vertexId!.Value);

        vertex.OwnerPlayerId = usuarioId;
        vertex.BuildingType = "aldeia";
        actingPlayer.Pontos += 1;
        actingPlayer.RemainingSettlements = Math.Max(0, actingPlayer.RemainingSettlements - 1);

        session.LastPlacedSettlementVertexId = vertex.VertexId;
        session.AwaitingInitialRoadPlacement = true;
        session.PendingInitialRoadFromVertexId = vertex.VertexId;

        if (IsSecondSetupSettlementPlacement(session))
        {
            GrantSetupResourcesFromVertex(session, actingPlayer, vertex);
        }

        return null;
    }

    private static string? ValidateInitialSettlementPlacement(CatanGameSessionState session, int? vertexId)
    {
        if (session.AwaitingInitialRoadPlacement)
        {
            return "Você precisa posicionar a estrada inicial antes de colocar outra aldeia.";
        }

        if (vertexId is null)
        {
            return "Informe um vértice para posicionar a aldeia.";
        }

        var vertex = session.Board.Vertices.FirstOrDefault(item => item.VertexId == vertexId.Value);
        if (vertex is null || vertex.VertexId == 0)
        {
            return "Vértice inválido.";
        }

        if (vertex.OwnerPlayerId is not null)
        {
            return "Esse vértice já possui construção.";
        }

        if (HasAdjacentSettlement(session, vertex.VertexId))
        {
            return "Não é possível posicionar uma aldeia ao lado de outra já construída.";
        }

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

    private static LobbyOperationResult<GameSessionResponse>? TryRollDice(CatanGameSessionState session)
    {
        if (session.HasRolledDiceThisTurn)
        {
            return LobbyOperationResult<GameSessionResponse>.Validation("Você já rolou os dados neste turno.");
        }

        var dice1 = Random.Shared.Next(1, 7);
        var dice2 = Random.Shared.Next(1, 7);

        session.LastDice1 = dice1;
        session.LastDice2 = dice2;
        session.HasRolledDiceThisTurn = true;
        session.LastRollResourceGains = [];
        session.PendingDiscardByPlayerId.Clear();

        var rolledTotal = dice1 + dice2;
        if (rolledTotal == 7)
        {
            session.ActiveTradeOffers.Clear();
            InitializePendingDiscardsForRobberRoll(session);
        }
        else
        {
            DistributeResourcesForRoll(session, rolledTotal);
        }

        var resourceGains = session.LastRollResourceGains
            .Select(gain => new CatanResourceGainState
            {
                UsuarioId = gain.UsuarioId,
                ResourceType = gain.ResourceType,
                Amount = gain.Amount
            })
            .ToList();

        session.RollHistory.Add(new CatanRollHistoryEntryState
        {
            RolledAtUtc = DateTime.UtcNow,
            CurrentTurnPlayerId = session.CurrentPlayerId,
            Dice1 = dice1,
            Dice2 = dice2,
            Total = rolledTotal,
            ResourceGains = resourceGains
        });

        if (session.RollHistory.Count > 20)
        {
            session.RollHistory.RemoveAt(0);
        }

        return null;
    }

    private static LobbyOperationResult<GameSessionResponse>? TryEndTurn(CatanGameSessionState session)
    {
        if (!session.HasRolledDiceThisTurn)
        {
            return LobbyOperationResult<GameSessionResponse>.Validation("Você precisa rolar os dados antes de passar o turno.");
        }

        if (HasAnyPendingDiscards(session))
        {
            return LobbyOperationResult<GameSessionResponse>.Validation("Ainda existem descartes pendentes após o resultado 7.");
        }

        var currentIndex = session.Players.FindIndex(player => player.UsuarioId == session.CurrentPlayerId);
        var nextIndex = (currentIndex + 1) % session.Players.Count;
        session.CurrentPlayerId = session.Players[nextIndex].UsuarioId;
        session.HasRolledDiceThisTurn = false;

        return null;
    }

    private static LobbyOperationResult<GameSessionResponse>? TryBuyDevelopmentCard(
        CatanGameSessionState session,
        CatanPlayerState actingPlayer)
    {
        EnsureBankInitialized(session);

        if (session.Bank.DevelopmentCardDeck.Count == 0 || session.Bank.DevelopmentCardCount <= 0)
        {
            return LobbyOperationResult<GameSessionResponse>.Validation("Não há mais cartas de desenvolvimento disponíveis no banco.");
        }

        if (!HasRequiredResources(actingPlayer, DevelopmentCardCost))
        {
            return LobbyOperationResult<GameSessionResponse>.Validation("Você não possui os recursos necessários para comprar uma carta de desenvolvimento.");
        }

        foreach (var cost in DevelopmentCardCost)
        {
            actingPlayer.Resources[cost.ResourceType] -= cost.Amount;
            ReturnToBank(session, cost.ResourceType, cost.Amount);
        }

        var developmentCardType = session.Bank.DevelopmentCardDeck[^1];
        session.Bank.DevelopmentCardDeck.RemoveAt(session.Bank.DevelopmentCardDeck.Count - 1);
        session.Bank.DevelopmentCardCount = session.Bank.DevelopmentCardDeck.Count;

        actingPlayer.DevelopmentCards ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        actingPlayer.DevelopmentCards.TryGetValue(developmentCardType, out var ownedCount);
        actingPlayer.DevelopmentCards[developmentCardType] = ownedCount + 1;

        return null;
    }

    private static LobbyOperationResult<GameSessionResponse>? TryBuildRoad(
        CatanGameSessionState session,
        CatanPlayerState actingPlayer,
        int? smallerVertexId,
        int? biggerVertexId)
    {
        if (smallerVertexId is null || biggerVertexId is null)
            return LobbyOperationResult<GameSessionResponse>.Validation("Informe os vértices da aresta para construir a estrada.");

        if (!HasRequiredResources(actingPlayer, RoadCost))
            return LobbyOperationResult<GameSessionResponse>.Validation("Recursos insuficientes para construir uma estrada.");

        if (actingPlayer.RemainingRoads <= 0)
            return LobbyOperationResult<GameSessionResponse>.Validation("Você não possui mais peças de estrada disponíveis.");

        var edgeKey = new EdgeKey(smallerVertexId.Value, biggerVertexId.Value);
        var edge = session.Board.Edges.FirstOrDefault(e =>
            e.EdgeKey.smallerVertexId == edgeKey.smallerVertexId &&
            e.EdgeKey.biggerVertexId == edgeKey.biggerVertexId);

        if (edge is null)
            return LobbyOperationResult<GameSessionResponse>.Validation("Aresta inválida.");

        if (edge.OwnerPlayerId is not null)
            return LobbyOperationResult<GameSessionResponse>.Validation("Essa aresta já possui uma estrada.");

        if (!IsValidRoadPlacement(session.Board, actingPlayer.UsuarioId, edgeKey))
            return LobbyOperationResult<GameSessionResponse>.Validation("A estrada deve ser conectada à sua rede de construções.");

        foreach (var cost in RoadCost)
        {
            actingPlayer.Resources[cost.ResourceType] -= cost.Amount;
            ReturnToBank(session, cost.ResourceType, cost.Amount);
        }

        edge.OwnerPlayerId = actingPlayer.UsuarioId;
        actingPlayer.RemainingRoads = Math.Max(0, actingPlayer.RemainingRoads - 1);

        return null;
    }

    private static LobbyOperationResult<GameSessionResponse>? TryBuildVillage(
        CatanGameSessionState session,
        CatanPlayerState actingPlayer,
        int? vertexId)
    {
        if (vertexId is null)
            return LobbyOperationResult<GameSessionResponse>.Validation("Informe um vértice para construir o vilarejo.");

        if (!HasRequiredResources(actingPlayer, VillageCost))
            return LobbyOperationResult<GameSessionResponse>.Validation("Recursos insuficientes para construir um vilarejo.");

        if (actingPlayer.RemainingSettlements <= 0)
            return LobbyOperationResult<GameSessionResponse>.Validation("Você não possui mais peças de vilarejo disponíveis.");

        var vertex = session.Board.Vertices.FirstOrDefault(v => v.VertexId == vertexId.Value);
        if (vertex is null)
            return LobbyOperationResult<GameSessionResponse>.Validation("Vértice inválido.");

        if (vertex.OwnerPlayerId is not null)
            return LobbyOperationResult<GameSessionResponse>.Validation("Esse vértice já está ocupado.");

        if (HasAdjacentSettlement(session, vertexId.Value))
            return LobbyOperationResult<GameSessionResponse>.Validation("Não é possível construir um vilarejo adjacente a outra construção (regra de distância).");

        var playerRoadEndpoints = session.Board.Edges
            .Where(e => e.OwnerPlayerId == actingPlayer.UsuarioId)
            .SelectMany(e => new[] { e.EdgeKey.smallerVertexId, e.EdgeKey.biggerVertexId })
            .ToHashSet();

        if (!playerRoadEndpoints.Contains(vertexId.Value))
            return LobbyOperationResult<GameSessionResponse>.Validation("O vilarejo deve estar conectado à sua rede de estradas.");

        foreach (var cost in VillageCost)
        {
            actingPlayer.Resources[cost.ResourceType] -= cost.Amount;
            ReturnToBank(session, cost.ResourceType, cost.Amount);
        }

        vertex.OwnerPlayerId = actingPlayer.UsuarioId;
        vertex.BuildingType = SettlementBuildingType;
        actingPlayer.Pontos += 1;
        actingPlayer.RemainingSettlements = Math.Max(0, actingPlayer.RemainingSettlements - 1);

        return null;
    }

    private static LobbyOperationResult<GameSessionResponse>? TryBuildCity(
        CatanGameSessionState session,
        CatanPlayerState actingPlayer,
        int? vertexId)
    {
        if (vertexId is null)
            return LobbyOperationResult<GameSessionResponse>.Validation("Informe um vértice para construir a cidade.");

        if (!HasRequiredResources(actingPlayer, CityCost))
            return LobbyOperationResult<GameSessionResponse>.Validation("Recursos insuficientes para construir uma cidade.");

        if (actingPlayer.RemainingCities <= 0)
            return LobbyOperationResult<GameSessionResponse>.Validation("Você não possui mais peças de cidade disponíveis.");

        var vertex = session.Board.Vertices.FirstOrDefault(v => v.VertexId == vertexId.Value);
        if (vertex is null)
            return LobbyOperationResult<GameSessionResponse>.Validation("Vértice inválido.");

        if (vertex.OwnerPlayerId != actingPlayer.UsuarioId)
            return LobbyOperationResult<GameSessionResponse>.Validation("Você só pode construir uma cidade sobre um vilarejo próprio.");

        if (!string.Equals(vertex.BuildingType, SettlementBuildingType, StringComparison.OrdinalIgnoreCase))
            return LobbyOperationResult<GameSessionResponse>.Validation("Você só pode construir uma cidade sobre um vilarejo.");

        foreach (var cost in CityCost)
        {
            actingPlayer.Resources[cost.ResourceType] -= cost.Amount;
            ReturnToBank(session, cost.ResourceType, cost.Amount);
        }

        vertex.BuildingType = CityBuildingType;
        actingPlayer.Pontos += 1;
        actingPlayer.RemainingCities = Math.Max(0, actingPlayer.RemainingCities - 1);
        actingPlayer.RemainingSettlements = Math.Min(5, actingPlayer.RemainingSettlements + 1);

        return null;
    }

    private static bool IsValidRoadPlacement(CatanBoardState board, int playerId, EdgeKey targetEdge)
    {
        var edgesByVertex = BuildEdgesByVertex(board);
        var vertexById = board.Vertices.ToDictionary(v => v.VertexId);
        var playerEdgeKeys = board.Edges
            .Where(e => e.OwnerPlayerId == playerId)
            .Select(e => e.EdgeKey)
            .ToHashSet();

        return IsReachableRoadEndpoint(targetEdge.smallerVertexId, targetEdge, playerId, vertexById, edgesByVertex, playerEdgeKeys)
            || IsReachableRoadEndpoint(targetEdge.biggerVertexId, targetEdge, playerId, vertexById, edgesByVertex, playerEdgeKeys);
    }

    private static bool IsReachableRoadEndpoint(
        int vertexId,
        EdgeKey targetEdge,
        int playerId,
        Dictionary<int, CatanVertexState> vertexById,
        Dictionary<int, List<EdgeKey>> edgesByVertex,
        HashSet<EdgeKey> playerEdgeKeys)
    {
        if (!vertexById.TryGetValue(vertexId, out var vertex))
            return false;

        if (vertex.OwnerPlayerId == playerId)
            return true;

        if (vertex.OwnerPlayerId is not null)
            return false;

        if (!edgesByVertex.TryGetValue(vertexId, out var adjacentEdges))
            return false;

        return adjacentEdges.Any(e => e != targetEdge && playerEdgeKeys.Contains(e));
    }

    private static Dictionary<int, List<EdgeKey>> BuildEdgesByVertex(CatanBoardState board)
    {
        var result = new Dictionary<int, List<EdgeKey>>();
        foreach (var edge in board.Edges)
        {
            var key = edge.EdgeKey;
            if (!result.TryGetValue(key.smallerVertexId, out var list1))
                result[key.smallerVertexId] = list1 = [];
            list1.Add(key);
            if (!result.TryGetValue(key.biggerVertexId, out var list2))
                result[key.biggerVertexId] = list2 = [];
            list2.Add(key);
        }
        return result;
    }

    private static LobbyOperationResult<GameSessionResponse>? TryOfferTrade(
        CatanGameSessionState session,
        CatanPlayerState actingPlayer,
        GameActionRequest request)
    {
        var offeredResources = NormalizeTradeResources(request.OfferedResources);
        var askedResources = NormalizeTradeResources(request.AskedResources);

        if (offeredResources.Count == 0 || askedResources.Count == 0)
        {
            return LobbyOperationResult<GameSessionResponse>.Validation("A troca precisa ter recursos oferecidos e pedidos.");
        }

        foreach (var offeredResource in offeredResources)
        {
            var availableAmount = actingPlayer.Resources.GetValueOrDefault(offeredResource.Key);
            if (offeredResource.Value > availableAmount)
            {
                return LobbyOperationResult<GameSessionResponse>.Validation("Você não possui recursos suficientes para essa oferta.");
            }
        }

        var createdAtUtc = DateTime.UtcNow;
        session.ActiveTradeOffers.Add(new CatanTradeOfferState
        {
            OfferId = session.NextTradeOfferId++,
            OffererPlayerId = actingPlayer.UsuarioId,
            OfferedResources = offeredResources,
            AskedResources = askedResources,
            CreatedAtUtc = createdAtUtc,
            ExpiresAtUtc = createdAtUtc.Add(TradeOfferLifetime)
        });

        return null;
    }

    private static LobbyOperationResult<GameSessionResponse>? TryTradeWithBank(
        CatanGameSessionState session,
        CatanPlayerState actingPlayer,
        GameActionRequest request)
    {
        var offeredResources = NormalizeTradeResources(request.OfferedResources);
        var askedResources = NormalizeTradeResources(request.AskedResources);

        if (offeredResources.Count == 0 || askedResources.Count == 0)
        {
            return LobbyOperationResult<GameSessionResponse>.Validation("A troca com o banco precisa ter recursos oferecidos e pedidos.");
        }

        var rates = ComputeBankTradeRatesForPlayer(session, actingPlayer);
        var offeredTradeUnits = 0;

        foreach (var offeredResource in offeredResources)
        {
            if (!rates.TryGetValue(offeredResource.Key, out var tradeRate))
            {
                return LobbyOperationResult<GameSessionResponse>.Validation("A troca contém um recurso inválido.");
            }

            var availableAmount = actingPlayer.Resources.GetValueOrDefault(offeredResource.Key);
            if (offeredResource.Value > availableAmount)
            {
                return LobbyOperationResult<GameSessionResponse>.Validation("Você não possui recursos suficientes para trocar com o banco.");
            }

            if (tradeRate <= 0 || offeredResource.Value % tradeRate != 0)
            {
                return LobbyOperationResult<GameSessionResponse>.Validation("As quantidades oferecidas não respeitam as taxas de troca do banco.");
            }

            offeredTradeUnits += offeredResource.Value / tradeRate;
        }

        if (offeredTradeUnits <= 0)
        {
            return LobbyOperationResult<GameSessionResponse>.Validation("A troca com o banco precisa gerar pelo menos um crédito de troca.");
        }

        EnsureBankInitialized(session);

        var askedTradeUnits = 0;
        foreach (var askedResource in askedResources)
        {
            if (!TradableResources.Contains(askedResource.Key, StringComparer.OrdinalIgnoreCase))
            {
                return LobbyOperationResult<GameSessionResponse>.Validation("A troca contém um recurso inválido.");
            }

            askedTradeUnits += askedResource.Value;

            var bankAmount = session.Bank.ResourceCounts.GetValueOrDefault(askedResource.Key);
            if (askedResource.Value > bankAmount)
            {
                return LobbyOperationResult<GameSessionResponse>.Validation("O banco não possui recursos suficientes para essa troca.");
            }
        }

        if (askedTradeUnits != offeredTradeUnits)
        {
            return LobbyOperationResult<GameSessionResponse>.Validation("A troca precisa respeitar exatamente as taxas do banco.");
        }

        foreach (var offeredResource in offeredResources)
        {
            actingPlayer.Resources.TryGetValue(offeredResource.Key, out var currentAmount);
            actingPlayer.Resources[offeredResource.Key] = currentAmount - offeredResource.Value;
            ReturnToBank(session, offeredResource.Key, offeredResource.Value);
        }

        foreach (var askedResource in askedResources)
        {
            if (!TryWithdrawFromBank(session, askedResource.Key, askedResource.Value))
            {
                return LobbyOperationResult<GameSessionResponse>.Conflict("Falha ao retirar recursos do banco para concluir a troca.");
            }

            actingPlayer.Resources.TryGetValue(askedResource.Key, out var currentAmount);
            actingPlayer.Resources[askedResource.Key] = currentAmount + askedResource.Value;
        }

        return null;
    }

    private static Dictionary<string, int> NormalizeTradeResources(IReadOnlyDictionary<string, int>? resources)
    {
        return (resources ?? new Dictionary<string, int>())
            .Where(resource => !string.IsNullOrWhiteSpace(resource.Key) && resource.Value > 0)
            .GroupBy(resource => resource.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(resource => resource.Value),
                StringComparer.OrdinalIgnoreCase);
    }

    private static bool RemoveExpiredTradeOffers(CatanGameSessionState session, DateTime nowUtc)
    {
        return session.ActiveTradeOffers.RemoveAll(offer => offer.ExpiresAtUtc <= nowUtc) > 0;
    }

    private static void EnsurePendingDiscardStateInitialized(CatanGameSessionState session)
    {
        session.PendingDiscardByPlayerId ??= [];
    }

    private static void EnsureBankInitialized(CatanGameSessionState session)
    {
        session.Bank ??= CatanBankState.CreateDefault();

        session.Bank.ResourceCounts ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in TradableResources)
        {
            if (!session.Bank.ResourceCounts.ContainsKey(resource))
            {
                session.Bank.ResourceCounts[resource] = CatanBankState.InitialResourceCountPerType;
            }
        }

        if (session.Bank.DevelopmentCardCount < 0)
        {
            session.Bank.DevelopmentCardCount = 0;
        }

        session.Bank.DevelopmentCardDeck ??= [];
        if (session.Bank.DevelopmentCardDeck.Count == 0 && session.Bank.DevelopmentCardCount > 0)
        {
            InitializeDevelopmentDeck(session.Bank, session.Bank.DevelopmentCardCount);
        }
        else
        {
            session.Bank.DevelopmentCardCount = session.Bank.DevelopmentCardDeck.Count;
        }

        foreach (var player in session.Players)
        {
            player.DevelopmentCards ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        EnsurePendingDiscardStateInitialized(session);
    }

    private static void InitializePendingDiscardsForRobberRoll(CatanGameSessionState session)
    {
        EnsurePendingDiscardStateInitialized(session);
        session.PendingDiscardByPlayerId.Clear();

        foreach (var player in session.Players)
        {
            var resourceCount = player.Resources?.Values.Sum() ?? 0;
            if (resourceCount < 8)
            {
                continue;
            }

            var requiredDiscardCount = resourceCount / 2;
            if (requiredDiscardCount > 0)
            {
                session.PendingDiscardByPlayerId[player.UsuarioId] = requiredDiscardCount;
            }
        }
    }

    private static bool HasAnyPendingDiscards(CatanGameSessionState session)
    {
        EnsurePendingDiscardStateInitialized(session);
        return session.PendingDiscardByPlayerId.Any(entry => entry.Value > 0);
    }

    private static int GetPendingDiscardAmountForPlayer(CatanGameSessionState session, int usuarioId)
    {
        EnsurePendingDiscardStateInitialized(session);
        return session.PendingDiscardByPlayerId.TryGetValue(usuarioId, out var amount) && amount > 0
            ? amount
            : 0;
    }

    private static LobbyOperationResult<GameSessionResponse>? TryDiscardResources(
        CatanGameSessionState session,
        CatanPlayerState actingPlayer,
        IReadOnlyDictionary<string, int>? offeredResources)
    {
        var requiredDiscardCount = GetPendingDiscardAmountForPlayer(session, actingPlayer.UsuarioId);
        if (requiredDiscardCount <= 0)
        {
            return LobbyOperationResult<GameSessionResponse>.Validation("Você não possui descarte pendente.");
        }

        var normalizedDiscard = NormalizeTradeResources(offeredResources);
        var totalDiscarded = normalizedDiscard.Values.Sum();
        if (totalDiscarded != requiredDiscardCount)
        {
            return LobbyOperationResult<GameSessionResponse>.Validation(
                $"Você deve descartar exatamente {requiredDiscardCount} carta(s).");
        }

        foreach (var discard in normalizedDiscard)
        {
            if (!TradableResources.Contains(discard.Key, StringComparer.OrdinalIgnoreCase))
            {
                return LobbyOperationResult<GameSessionResponse>.Validation("O descarte contém um recurso inválido.");
            }

            var playerAmount = actingPlayer.Resources.GetValueOrDefault(discard.Key);
            if (discard.Value > playerAmount)
            {
                return LobbyOperationResult<GameSessionResponse>.Validation(
                    "Você não possui recursos suficientes para esse descarte.");
            }
        }

        foreach (var discard in normalizedDiscard)
        {
            actingPlayer.Resources.TryGetValue(discard.Key, out var currentAmount);
            actingPlayer.Resources[discard.Key] = currentAmount - discard.Value;
            ReturnToBank(session, discard.Key, discard.Value);
        }

        session.PendingDiscardByPlayerId.Remove(actingPlayer.UsuarioId);
        return null;
    }

    private static void InitializeDevelopmentDeck(CatanBankState bank, int? remainingCardCount = null)
    {
        var deck = new List<string>(CatanBankState.InitialDevelopmentCardCount);
        deck.AddRange(Enumerable.Repeat(DevelopmentCardTypes.Knight, 14));
        deck.AddRange(Enumerable.Repeat(DevelopmentCardTypes.VictoryPoint, 5));
        deck.AddRange(Enumerable.Repeat(DevelopmentCardTypes.RoadBuilder, 2));
        deck.AddRange(Enumerable.Repeat(DevelopmentCardTypes.Plus2Resources, 2));
        deck.AddRange(Enumerable.Repeat(DevelopmentCardTypes.Monopoly, 2));

        for (var index = deck.Count - 1; index > 0; index--)
        {
            var swapIndex = Random.Shared.Next(index + 1);
            (deck[index], deck[swapIndex]) = (deck[swapIndex], deck[index]);
        }

        var count = Math.Clamp(remainingCardCount ?? deck.Count, 0, deck.Count);
        bank.DevelopmentCardDeck = deck.Take(count).ToList();
        bank.DevelopmentCardCount = bank.DevelopmentCardDeck.Count;
    }

    private static bool HasRequiredResources(
        CatanPlayerState player,
        IEnumerable<(string ResourceType, int Amount)> requiredResources)
    {
        return requiredResources.All(resource =>
            player.Resources.GetValueOrDefault(resource.ResourceType) >= resource.Amount);
    }

    private static bool TryWithdrawFromBank(CatanGameSessionState session, string resourceType, int amount = 1)
    {
        if (string.IsNullOrWhiteSpace(resourceType) || amount <= 0)
        {
            return false;
        }

        EnsureBankInitialized(session);

        if (!session.Bank.ResourceCounts.TryGetValue(resourceType, out var currentAmount) || currentAmount < amount)
        {
            return false;
        }

        session.Bank.ResourceCounts[resourceType] = currentAmount - amount;
        return true;
    }

    private static void ReturnToBank(CatanGameSessionState session, string resourceType, int amount = 1)
    {
        if (string.IsNullOrWhiteSpace(resourceType) || amount <= 0)
        {
            return;
        }

        EnsureBankInitialized(session);
        session.Bank.ResourceCounts.TryGetValue(resourceType, out var currentAmount);
        session.Bank.ResourceCounts[resourceType] = currentAmount + amount;
    }

    private static void DistributeResourcesForRoll(CatanGameSessionState session, int rolledTotal)
    {
        var playersById = session.Players.ToDictionary(player => player.UsuarioId);
        var gainsByPlayerAndResource = new Dictionary<(int UsuarioId, string ResourceType), int>();

        EnsureBankInitialized(session);

        var producingTiles = session.Board.Tiles.Where(tile =>
            tile.NumberToken == rolledTotal &&
            !string.Equals(tile.ResourceType, DesertResourceType, StringComparison.OrdinalIgnoreCase));

        foreach (var tile in producingTiles)
        {
            foreach (var vertex in GetVerticesAdjacentToTile(session.Board, tile))
            {
                if (vertex.OwnerPlayerId is null)
                    continue;

                var isSettlement = string.Equals(vertex.BuildingType, SettlementBuildingType, StringComparison.OrdinalIgnoreCase);
                var isCity = string.Equals(vertex.BuildingType, CityBuildingType, StringComparison.OrdinalIgnoreCase);

                if (!isSettlement && !isCity)
                    continue;

                if (!playersById.TryGetValue(vertex.OwnerPlayerId.Value, out var owner))
                    continue;

                var production = isCity ? 2 : 1;
                var withdrawn = 0;
                for (var p = 0; p < production; p++)
                {
                    if (!TryWithdrawFromBank(session, tile.ResourceType))
                        break;
                    withdrawn++;
                }

                if (withdrawn == 0)
                    continue;

                owner.Resources.TryGetValue(tile.ResourceType, out var currentAmount);
                owner.Resources[tile.ResourceType] = currentAmount + withdrawn;

                var gainKey = (owner.UsuarioId, tile.ResourceType);
                gainsByPlayerAndResource.TryGetValue(gainKey, out var gainedAmount);
                gainsByPlayerAndResource[gainKey] = gainedAmount + withdrawn;
            }
        }

        session.LastRollResourceGains = gainsByPlayerAndResource
            .Select(entry => new CatanResourceGainState
            {
                UsuarioId = entry.Key.UsuarioId,
                ResourceType = entry.Key.ResourceType,
                Amount = entry.Value
            })
            .ToList();
    }

    private static IEnumerable<CatanVertexState> GetVerticesAdjacentToTile(CatanBoardState board, CatanTileState tile)
    {
        foreach (var vertex in board.Vertices)
        {
            var dx = vertex.Position.X - tile.CenterX;
            var dy = vertex.Position.Y - tile.CenterY;
            var distance = Math.Sqrt((dx * dx) + (dy * dy));

            if (Math.Abs(distance - HexRadius) <= VertexAdjacencyTolerance)
            {
                yield return vertex;
            }
        }
    }

    private static bool IsSecondSetupSettlementPlacement(CatanGameSessionState session)
    {
        if (session.SetupTurnOrder.Count == 0)
        {
            return false;
        }

        var secondPlacementStart = session.SetupTurnOrder.Count / 2;
        return session.SetupStepIndex >= secondPlacementStart;
    }

    private static void GrantSetupResourcesFromVertex(CatanGameSessionState session, CatanPlayerState player, CatanVertexState vertex)
    {
        EnsureBankInitialized(session);

        foreach (var resourceType in vertex.Resources)
        {
            if (string.IsNullOrWhiteSpace(resourceType)
                || string.Equals(resourceType, DesertResourceType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryWithdrawFromBank(session, resourceType))
            {
                continue;
            }

            player.Resources.TryGetValue(resourceType, out var currentAmount);
            player.Resources[resourceType] = currentAmount + 1;
        }
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
        var edgeTouchCounts = new Dictionary<EdgeKey, int>();

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
                        edgeTouchCounts.TryGetValue(edgeKey, out var edgeTouchCount);
                        edgeTouchCounts[edgeKey] = edgeTouchCount + 1;

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
                            edgeTouchCounts.TryGetValue(edgeKey, out edgeTouchCount);
                            edgeTouchCounts[edgeKey] = edgeTouchCount + 1;

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

        AssignFixedPositionPorts(board, edgeTouchCounts);

        var debugvar = board.PortAnchors;

        return board;
    }

    private static void AssignFixedPositionPorts(CatanBoardState board, Dictionary<EdgeKey, int> edgeTouchCounts)
    {
        foreach (var vertex in board.Vertices)
        {
            vertex.Ports.Clear();
        }

        var coastalEdges = board.Edges
            .Where(edge => edgeTouchCounts.GetValueOrDefault(edge.EdgeKey) == 1)
            .OrderBy(edge => GetEdgeAngleFromBoardCenter(edge, board))
            .ToList();

        if (coastalEdges.Count == 0)
        {
            return;
        }

        var selectedSlotEdges = SelectFixedPortSlots(coastalEdges, FixedPortSlotCount);
        var shuffledPortTypes = PortTypePool.ToList();
        Random.Shared.Shuffle(CollectionsMarshal.AsSpan(shuffledPortTypes));

        var verticesById = board.Vertices.ToDictionary(vertex => vertex.VertexId);
        for (int i = 0; i < selectedSlotEdges.Count && i < shuffledPortTypes.Count; i++)
        {
            var slotEdge = selectedSlotEdges[i];
            var portType = shuffledPortTypes[i];

            TryAttachPort(verticesById, slotEdge.EdgeKey.smallerVertexId, portType);
            TryAttachPort(verticesById, slotEdge.EdgeKey.biggerVertexId, portType);
            board.PortAnchors.Add(new Port { A = slotEdge.PointA, B = slotEdge.PointB, Label = portType });
        }
    }

    private static List<CatanEdgeState> SelectFixedPortSlots(List<CatanEdgeState> coastalEdges, int desiredSlots)
    {
        var selected = new List<CatanEdgeState>();
        var chosenIndexes = new HashSet<int>();
        var slotsToSelect = Math.Min(desiredSlots, coastalEdges.Count);

        for (int slot = 0; slot < slotsToSelect; slot++)
        {
            var proportionalIndex = (int)Math.Floor(slot * (coastalEdges.Count / (double)slotsToSelect));
            var candidateIndex = Math.Clamp(proportionalIndex, 0, coastalEdges.Count - 1);

            if (!chosenIndexes.Add(candidateIndex))
            {
                candidateIndex = FindNextAvailableIndex(candidateIndex, coastalEdges.Count, chosenIndexes);
            }

            chosenIndexes.Add(candidateIndex);
            selected.Add(coastalEdges[candidateIndex]);
        }

        return selected;
    }

    private static int FindNextAvailableIndex(int startIndex, int total, HashSet<int> chosenIndexes)
    {
        for (int offset = 1; offset < total; offset++)
        {
            var nextIndex = (startIndex + offset) % total;
            if (!chosenIndexes.Contains(nextIndex))
            {
                return nextIndex;
            }
        }

        return startIndex;
    }

    private static double GetEdgeAngleFromBoardCenter(CatanEdgeState edge, CatanBoardState board)
    {
        var midpointX = (edge.PointA.X + edge.PointB.X) / 2.0;
        var midpointY = (edge.PointA.Y + edge.PointB.Y) / 2.0;
        var dx = midpointX - (board.width / 2.0);
        var dy = midpointY - (board.height / 2.0);

        var angle = Math.Atan2(dy, dx);
        return angle < 0 ? angle + (Math.PI * 2.0) : angle;
    }

    private static void TryAttachPort(Dictionary<int, CatanVertexState> verticesById, int vertexId, string portType)
    {
        if (!verticesById.TryGetValue(vertexId, out var vertex))
        {
            return;
        }

        if (!vertex.Ports.Contains(portType, StringComparer.OrdinalIgnoreCase))
        {
            vertex.Ports.Add(portType);
        }
    }

    private static Dictionary<string, int> ComputeBankTradeRatesForPlayer(CatanGameSessionState session, CatanPlayerState player)
    {
        var rates = TradableResources.ToDictionary(resource => resource, _ => DefaultBankTradeRate, StringComparer.OrdinalIgnoreCase);

        var ownedVertices = session.Board.Vertices
            .Where(vertex =>
                vertex.OwnerPlayerId == player.UsuarioId &&
                (string.Equals(vertex.BuildingType, SettlementBuildingType, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(vertex.BuildingType, CityBuildingType, StringComparison.OrdinalIgnoreCase)));

        foreach (var vertex in ownedVertices)
        {
            foreach (var portType in vertex.Ports)
            {
                ApplyPortTradeRate(rates, portType);
            }
        }

        return rates;
    }

    private static void ApplyPortTradeRate(Dictionary<string, int> rates, string? portType)
    {
        if (string.IsNullOrWhiteSpace(portType))
        {
            return;
        }

        switch (portType.Trim().ToLowerInvariant())
        {
            case "generic_3to1":
                foreach (var resource in TradableResources)
                {
                    rates[resource] = Math.Min(rates[resource], GenericHarborTradeRate);
                }
                return;
            case "wood_2to1":
                rates["madeira"] = Math.Min(rates["madeira"], SpecificHarborTradeRate);
                return;
            case "brick_2to1":
                rates["argila"] = Math.Min(rates["argila"], SpecificHarborTradeRate);
                return;
            case "sheep_2to1":
                rates["ovelha"] = Math.Min(rates["ovelha"], SpecificHarborTradeRate);
                return;
            case "wheat_2to1":
                rates["trigo"] = Math.Min(rates["trigo"], SpecificHarborTradeRate);
                return;
            case "ore_2to1":
                rates["pedra"] = Math.Min(rates["pedra"], SpecificHarborTradeRate);
                return;
            default:
                return;
        }
    }

    private static GameSessionResponse ToResponse(CatanGameSessionState session, int usuarioId)
    {
        EnsureBankInitialized(session);
        EnsurePendingDiscardStateInitialized(session);

        var currentPlayer = session.Players.First(player => player.UsuarioId == session.CurrentPlayerId);
        var canCurrentUserAct = currentPlayer.UsuarioId == usuarioId;
        var pendingDiscardForCurrentUser = GetPendingDiscardAmountForPlayer(session, usuarioId);
        var hasPendingDiscardForCurrentUser = pendingDiscardForCurrentUser > 0;
        var hasAnyPendingDiscards = HasAnyPendingDiscards(session);
        var isSetupPhase = session.Phase == GameTipoFase.SetupInicial;
        var canPlaceInitialSettlement = isSetupPhase && canCurrentUserAct && !session.AwaitingInitialRoadPlacement;
        var canPlaceInitialRoad = isSetupPhase && canCurrentUserAct && session.AwaitingInitialRoadPlacement && session.PendingInitialRoadFromVertexId is not null;
        var pendingRoadVertexId = session.PendingInitialRoadFromVertexId;

        var isTurnPhase = session.Phase == GameTipoFase.Turno;
        var canRollDice = isTurnPhase && canCurrentUserAct && !session.HasRolledDiceThisTurn && !hasAnyPendingDiscards;
        var canEndTurn = isTurnPhase && canCurrentUserAct && session.HasRolledDiceThisTurn && !hasAnyPendingDiscards;
        var canTradeWithBank = isTurnPhase && canCurrentUserAct && session.HasRolledDiceThisTurn && !hasAnyPendingDiscards;
        var canBuyDevelopmentCard = isTurnPhase
            && canCurrentUserAct
            && session.HasRolledDiceThisTurn
            && !hasAnyPendingDiscards
            && session.Bank.DevelopmentCardDeck.Count > 0
            && HasRequiredResources(currentPlayer, DevelopmentCardCost);
        var canBuildRoad = isTurnPhase && canCurrentUserAct && session.HasRolledDiceThisTurn
            && !hasAnyPendingDiscards
            && currentPlayer.RemainingRoads > 0
            && HasRequiredResources(currentPlayer, RoadCost);
        var canBuildVillage = isTurnPhase && canCurrentUserAct && session.HasRolledDiceThisTurn
            && !hasAnyPendingDiscards
            && currentPlayer.RemainingSettlements > 0
            && HasRequiredResources(currentPlayer, VillageCost);
        var canBuildCity = isTurnPhase && canCurrentUserAct && session.HasRolledDiceThisTurn
            && !hasAnyPendingDiscards
            && currentPlayer.RemainingCities > 0
            && HasRequiredResources(currentPlayer, CityCost)
            && session.Board.Vertices.Any(v => v.OwnerPlayerId == currentPlayer.UsuarioId
                && string.Equals(v.BuildingType, SettlementBuildingType, StringComparison.OrdinalIgnoreCase));

        var availableActions = new List<string>();
        if (hasPendingDiscardForCurrentUser)
        {
            availableActions.Add(GameActionTypes.DiscardResources);
        }
        else
        {
            if (canPlaceInitialSettlement)
            {
                availableActions.Add(GameActionTypes.PlaceInitialSettlement);
            }
            if (canPlaceInitialRoad)
            {
                availableActions.Add(GameActionTypes.PlaceInitialRoad);
            }
            if (canRollDice)
            {
                availableActions.Add(GameActionTypes.RollDice);
            }
            if (canEndTurn)
            {
                availableActions.Add(GameActionTypes.EndTurn);
            }
            if (canTradeWithBank)
            {
                availableActions.Add(GameActionTypes.TradeWithBank);
            }
            if (canBuyDevelopmentCard)
            {
                availableActions.Add(GameActionTypes.BuyDevelopmentCard);
            }
            if (canBuildRoad)
            {
                availableActions.Add(GameActionTypes.BuildRoad);
            }
            if (canBuildVillage)
            {
                availableActions.Add(GameActionTypes.BuildVillage);
            }
            if (canBuildCity)
            {
                availableActions.Add(GameActionTypes.BuildCity);
            }
        }

        var result = new GameSessionResponse
        {
            SalaId = session.SalaId,
            GameType = session.GameType,
            Phase = session.Phase,
            CurrentPlayerId = currentPlayer.UsuarioId,
            CurrentPlayerNome = currentPlayer.Nome,
            YourPlayerId = usuarioId,
            CanCurrentUserAct = canCurrentUserAct,
            AvailableActions = availableActions,
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
                    Resources = new Dictionary<string, int>(player.Resources),
                    DevelopmentCards = player.UsuarioId == usuarioId
                        ? new Dictionary<string, int>(player.DevelopmentCards, StringComparer.OrdinalIgnoreCase)
                        : [],
                    HiddenDevelopmentCardCount = player.UsuarioId == usuarioId
                        ? 0
                        : player.DevelopmentCards.Values.Sum(),
                    BankTradeRates = ComputeBankTradeRatesForPlayer(session, player)
                })
                .ToList(),
            CatanState = new CatanGameStateResponse
            {
                SetupStepIndex = session.SetupStepIndex,
                SetupTurnOrder = [.. session.SetupTurnOrder],
                LastPlacedSettlementVertexId = session.LastPlacedSettlementVertexId,
                AwaitingInitialRoadPlacement = session.AwaitingInitialRoadPlacement,
                PendingInitialRoadFromVertexId = pendingRoadVertexId,
                HasRolledDiceThisTurn = session.HasRolledDiceThisTurn,
                LastDice1 = session.LastDice1,
                LastDice2 = session.LastDice2,
                LastDiceTotal = session.LastDice1 is not null && session.LastDice2 is not null
                    ? session.LastDice1 + session.LastDice2
                    : null,
                Bank = new BankStateResponse
                {
                    ResourceCounts = new Dictionary<string, int>(session.Bank.ResourceCounts, StringComparer.OrdinalIgnoreCase),
                    DevelopmentCardCount = session.Bank.DevelopmentCardCount
                },
                PendingDiscardByPlayerId = session.PendingDiscardByPlayerId
                    .Where(entry => entry.Value > 0)
                    .ToDictionary(entry => entry.Key, entry => entry.Value),
                LastRollResourceGains = session.LastRollResourceGains
                    .Select(gain => new GameResourceGainResponse
                    {
                        UsuarioId = gain.UsuarioId,
                        PlayerNome = session.Players.FirstOrDefault(player => player.UsuarioId == gain.UsuarioId)?.Nome ?? string.Empty,
                        ResourceType = gain.ResourceType,
                        Amount = gain.Amount
                    })
                    .ToList(),
                RollHistory = session.RollHistory
                    .Select(entry => new RollHistoryEntryResponse
                    {
                        RolledAtUtc = entry.RolledAtUtc,
                        CurrentTurnPlayerId = entry.CurrentTurnPlayerId,
                        Dice1 = entry.Dice1,
                        Dice2 = entry.Dice2,
                        Total = entry.Total,
                        ResourceGains = entry.ResourceGains
                            .Select(gain => new GameResourceGainResponse
                            {
                                UsuarioId = gain.UsuarioId,
                                PlayerNome = session.Players.FirstOrDefault(player => player.UsuarioId == gain.UsuarioId)?.Nome ?? string.Empty,
                                ResourceType = gain.ResourceType,
                                Amount = gain.Amount
                            })
                            .ToList()
                    })
                    .ToList(),
                RobberTileId = session.Board.RobberTileId,
                ActiveTradeOffers = session.ActiveTradeOffers
                    .OrderByDescending(offer => offer.CreatedAtUtc)
                    .Select(offer => new TradeOfferResponse
                    {
                        OfferId = offer.OfferId,
                        OffererPlayerId = offer.OffererPlayerId,
                        OffererName = session.Players.FirstOrDefault(player => player.UsuarioId == offer.OffererPlayerId)?.Nome ?? string.Empty,
                        OffererColor = session.Players.FirstOrDefault(player => player.UsuarioId == offer.OffererPlayerId)?.Cor ?? string.Empty,
                        OfferedResources = new Dictionary<string, int>(offer.OfferedResources, StringComparer.OrdinalIgnoreCase),
                        AskedResources = new Dictionary<string, int>(offer.AskedResources, StringComparer.OrdinalIgnoreCase),
                        CreatedAtUtc = offer.CreatedAtUtc,
                        ExpiresAtUtc = offer.ExpiresAtUtc
                    })
                    .ToList(),
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
                        IsAvailableForAction = canPlaceInitialSettlement && ValidateInitialSettlementPlacement(session, vertex.VertexId) is null,
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
                    .ToList(),
                PortAnchors = session.Board.PortAnchors
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
