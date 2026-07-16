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
    public Dictionary<string, int> Resources { get; set; } = [];
}

public class GameActionRequest
{
    public string ActionType { get; set; } = string.Empty;
    public int? VertexId { get; set; }
    public int? EdgeId { get; set; }
}

public class GameActionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public GameSessionResponse? UpdatedSession { get; set; }
}

public class CatanGameStateResponse
{
    public int SetupStepIndex { get; set; }
    public List<int> SetupTurnOrder { get; set; } = [];
    public int? LastPlacedSettlementVertexId { get; set; }
    public int? RobberTileId { get; set; }
    public List<CatanTileResponse> Tiles { get; set; } = [];
    public List<CatanVertexResponse> Vertices { get; set; } = [];
    public List<CatanEdgeResponse> Edges { get; set; } = [];
    public int width { get; set; }
    public int height { get; set; }
}

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

public class CatanEdgeResponse
{
    public int EdgeId { get; set; }
    public int VertexAId { get; set; }
    public int VertexBId { get; set; }
    public int? OwnerPlayerId { get; set; }
    public bool IsAvailableForAction { get; set; }
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
