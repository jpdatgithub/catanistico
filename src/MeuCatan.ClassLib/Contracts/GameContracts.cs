namespace MeuCatan.ClassLib.Contracts;

public enum GameTipoFase
{
    SetupInicial = 0,
    Turno = 1,
    Finalizado = 2
}

public static class GameActionTypes
{
    public const string PlaceInitialSettlement = "place-initial-settlement";
    public const string PlaceInitialRoad = "place-initial-road";
    public const string PlayKnight = "play-knight";
    public const string PlayRoadBuilder = "play-road-builder";
    public const string PlayPlus2Resources = "play-plus2resources";
    public const string PlayMonopoly = "play-monopoly";
    public const string BuyDevelopmentCard = "buy-development-card";
    public const string BuildRoad = "build-road";
    public const string BuildVillage = "build-village";
    public const string BuildCity = "build-city";
    public const string RollDice = "roll-dice";
    public const string EndTurn = "end-turn";
    public const string OfferTrade = "offer-trade";
    public const string TradeWithBank = "trade-with-bank";
    public const string AcceptTrade = "accept-trade";
    public const string DeclineTrade = "decline-trade";
    public const string ExecuteTrade = "execute-trade";
    public const string EditTrade = "edit-trade";
    public const string DiscardResources = "discard-resources";
    public const string MoveRobber = "move-robber";
    public const string ChooseRobberVictim = "choose-robber-victim";
}

public static class DevelopmentCardTypes
{
    public const string Knight = "cavaleiro";
    public const string VictoryPoint = "ponto-de-vitoria";
    public const string RoadBuilder = "roadbuilder";
    public const string Plus2Resources = "plus2resources";
    public const string Monopoly = "monopoly";
}

public class GameSessionResponse
{
    public int SalaId { get; set; }
    public string GameType { get; set; } = LobbyTipoJogo.CatanBase;
    public GameTipoFase Phase { get; set; } = GameTipoFase.SetupInicial;
    public int CurrentPlayerId { get; set; }
    public string CurrentPlayerNome { get; set; } = string.Empty;
    public int YourPlayerId { get; set; }
    public bool CanCurrentUserAct { get; set; }
    public List<string> AvailableActions { get; set; } = [];
    public List<GamePlayerStateResponse> Players { get; set; } = [];
    public CatanGameStateResponse? CatanState { get; set; }
}

public class GamePlayerStateResponse
{
    public int UsuarioId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Cor { get; set; } = string.Empty;
    public int Pontos { get; set; }
    public bool IsCurrentTurn { get; set; }
    public int RemainingRoads { get; set; }
    public int RemainingSettlements { get; set; }
    public int RemainingCities { get; set; }
    public int MaiorEstradaContinua { get; set; }
    public int UsedKnightsCount { get; set; }
    public bool HasLongestRoad { get; set; }
    public bool HasLargestArmy { get; set; }
    public Dictionary<string, int> Resources { get; set; } = [];
    public Dictionary<string, int> DevelopmentCards { get; set; } = [];
    public int HiddenDevelopmentCardCount { get; set; }
    public Dictionary<string, int> BankTradeRates { get; set; } = [];
}

public class GameActionRequest
{
    public string ActionType { get; set; } = string.Empty;
    public long? OfferId { get; set; }
    public int? VertexId { get; set; }
    public int? TileId { get; set; }
    public int? TargetPlayerId { get; set; }
    public int? EdgeId { get; set; }
    public int? SmallerVertexId { get; set; }
    public int? BiggerVertexId { get; set; }
    public Dictionary<string, int> SelectedResources { get; set; } = [];
    public Dictionary<string, int> OfferedResources { get; set; } = [];
    public Dictionary<string, int> AskedResources { get; set; } = [];
}

public class TradeOfferResponse
{
    public long OfferId { get; set; }
    public int OffererPlayerId { get; set; }
    public string OffererName { get; set; } = string.Empty;
    public string OffererColor { get; set; } = string.Empty;
    public Dictionary<string, int> OfferedResources { get; set; } = [];
    public Dictionary<string, int> AskedResources { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public Dictionary<int, bool> AcceptedByPlayerId { get; set; } = [];
}

public class GameActionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public GameSessionResponse? UpdatedSession { get; set; }
}

public class GameChatMessageResponse
{
    public int SalaId { get; set; }
    public int UsuarioId { get; set; }
    public string UsuarioNome { get; set; } = string.Empty;
    public string Mensagem { get; set; } = string.Empty;
    public DateTime EnviadaEmUtc { get; set; }
}

public class CatanGameStateResponse
{
    public int SetupStepIndex { get; set; }
    public List<int> SetupTurnOrder { get; set; } = [];
    public int? LastPlacedSettlementVertexId { get; set; }
    public bool AwaitingInitialRoadPlacement { get; set; }
    public int? PendingInitialRoadFromVertexId { get; set; }
    public bool HasRolledDiceThisTurn { get; set; }
    public int PendingRoadBuilderRoads { get; set; }
    public int? LastDice1 { get; set; }
    public int? LastDice2 { get; set; }
    public int? LastDiceTotal { get; set; }
    public BankStateResponse? Bank { get; set; }
    public List<GameResourceGainResponse> LastRollResourceGains { get; set; } = [];
    public List<RollHistoryEntryResponse> RollHistory { get; set; } = [];
    public List<RobberTheftHistoryEntryResponse> RobberTheftHistory { get; set; } = [];
    public List<KnightPlayHistoryEntryResponse> KnightPlayHistory { get; set; } = [];
    public List<PlayerTradeHistoryEntryResponse> PlayerTradeHistory { get; set; } = [];
    public int? RobberTileId { get; set; }
    public Dictionary<int, int> PendingDiscardByPlayerId { get; set; } = [];
    public bool AwaitingRobberPlacement { get; set; }
    public int? PendingRobberTileId { get; set; }
    public List<int> PendingRobberVictimPlayerIds { get; set; } = [];
    public List<TradeOfferResponse> ActiveTradeOffers { get; set; } = [];
    public List<CatanTileResponse> Tiles { get; set; } = [];
    public List<CatanVertexResponse> Vertices { get; set; } = [];
    public List<CatanEdgeResponse> Edges { get; set; } = [];
    public int width { get; set; }
    public int height { get; set; }
    public List<Port> PortAnchors { get; set; } = [];
}

public sealed class Port
{
    public Point A { get; set; }
    public Point B { get; set; }
    public string Label { get; set; } = "3:1";
}

public class BankStateResponse
{
    public Dictionary<string, int> ResourceCounts { get; set; } = [];
    public int DevelopmentCardCount { get; set; }
}

public class GameResourceGainResponse
{
    public int UsuarioId { get; set; }
    public string PlayerNome { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public int Amount { get; set; }
}

public class RollHistoryEntryResponse
{
    public DateTime RolledAtUtc { get; set; }
    public int CurrentTurnPlayerId { get; set; }
    public int Dice1 { get; set; }
    public int Dice2 { get; set; }
    public int Total { get; set; }
    public List<GameResourceGainResponse> ResourceGains { get; set; } = [];
}

public class RobberTheftHistoryEntryResponse
{
    public DateTime OccurredAtUtc { get; set; }
    public int ThiefPlayerId { get; set; }
    public int VictimPlayerId { get; set; }
    public string VisibleResourceType { get; set; } = string.Empty;
}

public class KnightPlayHistoryEntryResponse
{
    public DateTime OccurredAtUtc { get; set; }
    public int PlayerId { get; set; }
}

public class PlayerTradeHistoryEntryResponse
{
    public DateTime OccurredAtUtc { get; set; }
    public int OffererPlayerId { get; set; }
    public int RecipientPlayerId { get; set; }
    public Dictionary<string, int> OfferedResources { get; set; } = [];
    public Dictionary<string, int> AskedResources { get; set; } = [];
}

public sealed record TradeOfferExecutionSelection(long OfferId, int TargetPlayerId);

public class CatanTileResponse
{
    public int TileId { get; set; }
    public string ResourceType { get; set; } = string.Empty;
    public int NumberToken { get; set; }
    public int CubeX { get; set; }
    public int CubeY { get; set; }
    public int CubeZ { get; set; }
}

public class CatanVertexResponse
{
    public int VertexId { get; set; }
    public int? OwnerPlayerId { get; set; }
    public string? BuildingType { get; set; }
    public bool IsAvailableForAction { get; set; }
    public Point Position { get; set; }
    public List<string> Resources { get; set; } = [];
    public List<string> Ports { get; set; } = [];
}

public readonly record struct EdgeKey
{
    public int smallerVertexId { get; }
    public int biggerVertexId { get; }

    public EdgeKey(int vertex1, int vertex2)
    {
        //o id menor será sempre o A
        if (vertex1 < vertex2)
        {
            smallerVertexId = vertex1;
            biggerVertexId = vertex2;
        }
        else
        {
            smallerVertexId = vertex2;
            biggerVertexId = vertex1;
        }
    }
}

public class CatanEdgeResponse
{
    public int smallerVertexId { get; set; }
    public int biggerVertexId { get; set; }
    public int? OwnerPlayerId { get; set; }
    public bool IsAvailableForAction { get; set; }
    public Point PointA { get; set; }
    public Point PointB { get; set; }
}

public struct Point
{
    public double X { get; set; }
    public double Y { get; set; }

    public Point(double x, double y)
    {
        X = x;
        Y = y;
    }
}
