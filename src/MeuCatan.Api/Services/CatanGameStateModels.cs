using MeuCatan.ClassLib.Contracts;
using System.Text.Json.Serialization;

namespace MeuCatan.Api.Services;

public sealed class CatanGameSessionState
{
    public int SalaId { get; set; }
    public string GameType { get; set; } = LobbyTipoJogo.CatanBase;
    public GameTipoFase Phase { get; set; } = GameTipoFase.SetupInicial;
    public int CurrentPlayerId { get; set; }
    public int SetupStepIndex { get; set; }
    public List<int> SetupTurnOrder { get; set; } = [];
    public int? LastPlacedSettlementVertexId { get; set; }
    public bool AwaitingInitialRoadPlacement { get; set; }
    public int? PendingInitialRoadFromVertexId { get; set; }
    public bool HasRolledDiceThisTurn { get; set; }
    public int? LastDice1 { get; set; }
    public int? LastDice2 { get; set; }
    public List<CatanResourceGainState> LastRollResourceGains { get; set; } = [];
    public List<CatanPlayerState> Players { get; set; } = [];
    public CatanBoardState Board { get; set; } = new();
}

public sealed class CatanResourceGainState
{
    public int UsuarioId { get; set; }
    public string ResourceType { get; set; } = string.Empty;
    public int Amount { get; set; }
}

public sealed class CatanPlayerState
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

public sealed class CatanBoardState
{
    public int? RobberTileId { get; set; }
    public List<CatanTileState> Tiles { get; set; } = [];
    public List<CatanVertexState> Vertices { get; set; } = [];
    public List<CatanEdgeState> Edges { get; set; } = [];
    public int width { get; set; } = 1000;
    public int height { get; set; } = 1000;
}

public sealed class CatanTileState
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

public sealed class CatanVertexState
{
    public int VertexId { get; set; }
    public int? OwnerPlayerId { get; set; }
    public string? BuildingType { get; set; }
    public Point Position { get; set; }
    public List<string> Resources { get; set; } = [];
    public List<string> Ports { get; set; } = [];
}

public sealed class CatanEdgeState
{
    public int SmallerVertexId { get; set; }
    public int BiggerVertexId { get; set; }
    public int? OwnerPlayerId { get; set; }
    public Point PointA { get; set; }
    public Point PointB { get; set; }

    [JsonIgnore]
    public EdgeKey EdgeKey
    {
        get => new(SmallerVertexId, BiggerVertexId);
        set
        {
            SmallerVertexId = value.smallerVertexId;
            BiggerVertexId = value.biggerVertexId;
        }
    }
}