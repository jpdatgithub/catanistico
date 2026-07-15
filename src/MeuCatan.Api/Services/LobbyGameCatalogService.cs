using MeuCatan.ClassLib.Contracts;

namespace MeuCatan.Api.Services;

public interface ILobbyGameCatalogService
{
    IReadOnlyList<LobbyJogoDisponivelResponse> ListarJogos();
    LobbyJogoDisponivelResponse? ObterJogo(string gameType);
}

public sealed class LobbyGameCatalogService : ILobbyGameCatalogService
{
    private readonly List<LobbyJogoDisponivelResponse> _jogos =
    [
        new()
        {
            GameType = LobbyTipoJogo.CatanBase,
            DisplayName = "Catan Base",
            MinPlayers = 1,
            MaxPlayers = 4,
            SupportsOptions = false
        }
    ];

    public IReadOnlyList<LobbyJogoDisponivelResponse> ListarJogos() => _jogos;

    public LobbyJogoDisponivelResponse? ObterJogo(string gameType)
    {
        return _jogos.FirstOrDefault(j =>
            string.Equals(j.GameType, gameType?.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
