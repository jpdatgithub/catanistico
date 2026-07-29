namespace MeuCatan.MudblazorWasmClient.Components.Catan;

internal static class PlayerColorUtils
{
    public static string ToHex(string? colorName)
    {
        if (string.IsNullOrWhiteSpace(colorName))
        {
            return "#475569";
        }

        return colorName.Trim().ToLowerInvariant() switch
        {
            "vermelho" => "#dc2626",
            "azul" => "#2563eb",
            "branco" => "#e2e8f0",
            "laranja" => "#f97316",
            _ => "#475569"
        };
    }

    public static string ToHex(IReadOnlyDictionary<int, string>? playerColorsById, int? playerId)
    {
        if (playerId is null)
        {
            return "#475569";
        }

        if (playerColorsById is not null && playerColorsById.TryGetValue(playerId.Value, out var colorName))
        {
            return ToHex(colorName);
        }

        return "#475569";
    }
}
