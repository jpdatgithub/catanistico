using System.Globalization;
using MeuCatan.ClassLib.Contracts;

namespace MeuCatan.MudblazorWasmClient.Components.Catan;

public static class CatanBoardLayoutBuilder
{
    public static CatanBoardSvgModel Build(CatanGameStateResponse state, double width = 1000, double height = 1000)
    {
        var centerX = width / 2.0;
        var centerY = height / 2.0;

        var raio = 100.0; // Raio do hexágono em pixels

        // // Projeta os tiles em coordenadas unitárias (raio=1) e depois escala
        // // para caber no SVG, mantendo sempre (0,0,0) no centro.
        // var projectedTiles = state.Tiles
        //     .Select(tile =>
        //     {
        //         var q = tile.CubeX;
        //         var r = tile.CubeZ;
        //         var unitCenterX = Math.Sqrt(3) * (q + r / 2.0);
        //         var unitCenterY = 1.5 * r;

        //         return new ProjectedTile(tile, unitCenterX, unitCenterY);
        //     })
        //     .ToList();

        // var unitExtents = ComputeUnitExtents(projectedTiles);
        // var availableHalfWidth = width * 0.45;
        // var availableHalfHeight = height * 0.45;
        // var hexRadius = Math.Min(
        //     availableHalfWidth / Math.Max(unitExtents.MaxAbsX, 1.0),
        //     availableHalfHeight / Math.Max(unitExtents.MaxAbsY, 1.0));

        // var tiles = projectedTiles
        //     .Select(projected =>
        //     {
        //         var tile = projected.Tile;
        //         var tileCenterX = centerX + projected.UnitCenterX * hexRadius;
        //         var tileCenterY = centerY + projected.UnitCenterY * hexRadius;

        //         return new CatanSvgTile
        //         {
        //             TileId = tile.TileId,
        //             ResourceType = tile.ResourceType,
        //             NumberToken = tile.NumberToken,
        //             CenterX = tileCenterX,
        //             CenterY = tileCenterY,
        //             Points = BuildHexagonPoints(tileCenterX, tileCenterY, hexRadius),
        //             Fill = ResolveResourceColor(tile.ResourceType)
        //         };
        //     })
        //     .ToList();

        // var vertices = state.Vertices
        //     .Select(vertex =>
        //     {
        //         var angle = ((vertex.VertexId - 1) / 54.0) * Math.PI * 2;
        //         var ring = 1.0 + (((vertex.VertexId - 1) % 6) * 0.08);
        //         return new CatanSvgVertex
        //         {
        //             VertexId = vertex.VertexId,
        //             X = centerX + Math.Cos(angle) * hexRadius * 3.2 * ring,
        //             Y = centerY + Math.Sin(angle) * hexRadius * 2.6 * ring,
        //             OwnerPlayerId = vertex.OwnerPlayerId,
        //             BuildingType = vertex.BuildingType,
        //             IsAvailableForAction = vertex.IsAvailableForAction
        //         };
        //     })
        //     .ToDictionary(vertex => vertex.VertexId);

        // var edges = state.Edges
        //     .Select(edge =>
        //     {
        //         var vertexA = vertices[edge.VertexAId];
        //         var vertexB = vertices[edge.VertexBId];

        //         return new CatanSvgEdge
        //         {
        //             EdgeId = edge.EdgeId,
        //             X1 = vertexA.X,
        //             Y1 = vertexA.Y,
        //             X2 = vertexB.X,
        //             Y2 = vertexB.Y,
        //             OwnerPlayerId = edge.OwnerPlayerId,
        //             IsAvailableForAction = edge.IsAvailableForAction
        //         };
        //     })
        //     .ToList();
        var tiles = state.Tiles
            .Select(tile =>
            {
                double deslocamentoX = raio * (Math.Sqrt(3) * tile.CubeX + Math.Sqrt(3) / 2.0 * tile.CubeZ);
                double deslocamentoY = raio * (3.0 / 2.0 * tile.CubeZ);

                double tileCenterX = centerX + deslocamentoX;
                double tileCenterY = centerY + deslocamentoY;

                return new CatanSvgTile
                {
                    TileId = tile.TileId,
                    ResourceType = tile.ResourceType,
                    NumberToken = tile.NumberToken,
                    CenterX = tileCenterX,
                    CenterY = tileCenterY,
                    Points = CalcularPontosSvg(tileCenterX, tileCenterY, raio),
                    Fill = ResolveResourceColor(tile.ResourceType)
                };
            })
            .ToList();
        var vertices = new Dictionary<int, CatanSvgVertex>();
        var edges = new List<CatanSvgEdge>();

        return new CatanBoardSvgModel
        {
            Width = width,
            Height = height,
            Tiles = tiles,
            Vertices = vertices.Values.OrderBy(vertex => vertex.VertexId).ToList(),
            Edges = edges
        };
    }

    private static (double MaxAbsX, double MaxAbsY) ComputeUnitExtents(IEnumerable<ProjectedTile> projectedTiles)
    {
        var maxAbsX = 1.0;
        var maxAbsY = 1.0;

        foreach (var projected in projectedTiles)
        {
            foreach (var index in Enumerable.Range(0, 6))
            {
                var angle = Math.PI / 180.0 * (60 * index - 30);
                var x = projected.UnitCenterX + Math.Cos(angle);
                var y = projected.UnitCenterY + Math.Sin(angle);
                maxAbsX = Math.Max(maxAbsX, Math.Abs(x));
                maxAbsY = Math.Max(maxAbsY, Math.Abs(y));
            }
        }

        return (maxAbsX, maxAbsY);
    }

    private readonly record struct ProjectedTile(
        CatanTileResponse Tile,
        double UnitCenterX,
        double UnitCenterY);

    public static string CalcularPontosSvg(double centerX, double centerY, double radius)
    {
        var pontos = new List<(double X, double Y)>();

        for (int i = 0; i < 6; i++)
        {
            // Avança 60 graus no sentido horário (negativo no ciclo trigonométrico)
            double anguloGraus = -30 - (i * 60);
            double anguloRadianos = anguloGraus * (Math.PI / 180.0);

            // Cálculo com inversão de Y para SVG
            double x = centerX + radius * 0.95 * Math.Cos(anguloRadianos);
            double y = centerY + radius * 0.95 * Math.Sin(anguloRadianos);

            // Arredonda para 1 casa decimal
            x = Math.Round(x, 1);
            y = Math.Round(y, 1);

            pontos.Add((x, y));
        }

        return string.Join(" ", pontos.Select(p => $"{p.X.ToString(CultureInfo.InvariantCulture)},{p.Y.ToString(CultureInfo.InvariantCulture)}"));
    }

    private static string ResolveResourceColor(string resourceType)
    {
        return resourceType.ToLowerInvariant() switch
        {
            "madeira" => "#4f7f39",
            "argila" => "#b25b32",
            "ovelha" => "#8bbf63",
            "trigo" => "#d6b85a",
            "pedra" => "#7f8c8d",
            "deserto" => "#d9c29c",
            _ => "#cfcfcf"
        };
    }
}

public sealed class CatanBoardSvgModel
{
    public double Width { get; init; }
    public double Height { get; init; }
    public List<CatanSvgTile> Tiles { get; init; } = [];
    public List<CatanSvgVertex> Vertices { get; init; } = [];
    public List<CatanSvgEdge> Edges { get; init; } = [];
}

public sealed class CatanSvgTile
{
    public int TileId { get; init; }
    public string ResourceType { get; init; } = string.Empty;
    public int NumberToken { get; init; }
    public double CenterX { get; init; }
    public double CenterY { get; init; }
    public string Points { get; init; } = string.Empty;
    public string Fill { get; init; } = "#cfcfcf";
}

public sealed class CatanSvgVertex
{
    public int VertexId { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public int? OwnerPlayerId { get; init; }
    public string? BuildingType { get; init; }
    public bool IsAvailableForAction { get; init; }
}

public sealed class CatanSvgEdge
{
    public int EdgeId { get; init; }
    public double X1 { get; init; }
    public double Y1 { get; init; }
    public double X2 { get; init; }
    public double Y2 { get; init; }
    public int? OwnerPlayerId { get; init; }
    public bool IsAvailableForAction { get; init; }
}
