using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MeuCatan.ClassLib.Contracts;

namespace MeuCatan.MudblazorWasmClient.Services
{
    public class ClienteAuthService
    {
        private readonly HttpClient _httpClient;
        private readonly ClienteAuthStateProvider _authStateProvider;

        public ClienteAuthService(HttpClient httpClient, ClienteAuthStateProvider authStateProvider)
        {
            _httpClient = httpClient;
            _authStateProvider = authStateProvider;
        }

        public async Task<(bool Success, string? ErrorMessage)> RegisterAsync(RegisterClientRequest request)
        {
            var response = await _httpClient.PostAsJsonAsync("api/cliente-auth/register", request);
            if (response.IsSuccessStatusCode)
            {
                return (true, null);
            }

            var error = await response.Content.ReadAsStringAsync();
            return (false, string.IsNullOrWhiteSpace(error) ? "Não foi possível registrar." : error);
        }

        public async Task<(bool Success, string? ErrorMessage)> LoginAsync(LoginClientRequest request)
        {
            var response = await _httpClient.PostAsJsonAsync("api/cliente-auth/login", request);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return (false, string.IsNullOrWhiteSpace(error) ? "Credenciais inválidas." : error);
            }

            var data = await response.Content.ReadFromJsonAsync<LoginClientResponse>();
            if (data is null || string.IsNullOrWhiteSpace(data.Token))
            {
                return (false, "Resposta de login inválida.");
            }

            await _authStateProvider.MarkUserAsAuthenticatedAsync(data.Token);
            return (true, null);
        }

        public async Task<(bool Success, string? ErrorMessage)> ContinueAsGuestAsync(string? nome = null)
        {
            var response = await _httpClient.PostAsJsonAsync("api/cliente-auth/guest", new GuestClientRequest
            {
                Nome = string.IsNullOrWhiteSpace(nome) ? null : nome.Trim()
            });

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return (false, string.IsNullOrWhiteSpace(error) ? "Não foi possível iniciar o modo convidado." : error);
            }

            var data = await response.Content.ReadFromJsonAsync<LoginClientResponse>();
            if (data is null || string.IsNullOrWhiteSpace(data.Token))
            {
                return (false, "Resposta inválida ao iniciar o modo convidado.");
            }

            await _authStateProvider.MarkUserAsAuthenticatedAsync(data.Token);
            return (true, null);
        }

        public async Task<string?> EnsureSessionAsync()
        {
            var token = await _authStateProvider.GetTokenAsync();
            if (!string.IsNullOrWhiteSpace(token))
            {
                return token;
            }

            var guestResult = await ContinueAsGuestAsync();
            if (!guestResult.Success)
            {
                return null;
            }

            return await _authStateProvider.GetTokenAsync();
        }

        public Task LogoutAsync()
        {
            return _authStateProvider.MarkUserAsLoggedOutAsync();
        }

        public async Task<ClientProfileResponse?> GetProfileAsync()
        {
            using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, "api/cliente-auth/me");
            if (request is null)
            {
                return null;
            }

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<ClientProfileResponse>();
        }

        public async Task<(bool Success, LobbyListarSalasResponse? Data, string? ErrorMessage)> ListarSalasAsync()
        {
            using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, "api/lobby/salas");
            if (request is null)
            {
                return (false, null, "Sessão inválida.");
            }

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return (false, null, await ReadErrorAsync(response, "Não foi possível carregar as salas."));
            }

            var data = await response.Content.ReadFromJsonAsync<LobbyListarSalasResponse>();
            return data is null
                ? (false, null, "Resposta inválida ao listar salas.")
                : (true, data, null);
        }

        public async Task<(bool Success, LobbyJogosDisponiveisResponse? Data, string? ErrorMessage)> ListarJogosDisponiveisAsync()
        {
            using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, "api/lobby/jogos");
            if (request is null)
            {
                return (false, null, "Sessão inválida.");
            }

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return (false, null, await ReadErrorAsync(response, "Não foi possível carregar os jogos disponíveis."));
            }

            var data = await response.Content.ReadFromJsonAsync<LobbyJogosDisponiveisResponse>();
            return data is null
                ? (false, null, "Resposta inválida ao listar jogos disponíveis.")
                : (true, data, null);
        }

        public async Task<(bool Success, LobbyCriarSalaResponse? Data, string? ErrorMessage)> CriarSalaAsync(LobbyCriarSalaRequest requestBody)
        {
            using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, "api/lobby/salas", requestBody);
            if (request is null)
            {
                return (false, null, "Sessão inválida.");
            }

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return (false, null, await ReadErrorAsync(response, "Não foi possível criar a sala."));
            }

            var data = await response.Content.ReadFromJsonAsync<LobbyCriarSalaResponse>();
            return data is null
                ? (false, null, "Resposta inválida ao criar sala.")
                : (true, data, null);
        }

        public async Task<(bool Success, LobbyDetalheSalaResponse? Data, string? ErrorMessage)> EntrarSalaAsync(int salaId, string? codigoPrivado)
        {
            using var request = await CreateAuthenticatedRequestAsync(
                HttpMethod.Post,
                $"api/lobby/salas/{salaId}/entrar",
                new LobbyEntrarSalaRequest
                {
                    CodigoPrivado = string.IsNullOrWhiteSpace(codigoPrivado) ? null : codigoPrivado.Trim()
                });

            if (request is null)
            {
                return (false, null, "Sessão inválida.");
            }

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return (false, null, await ReadErrorAsync(response, "Não foi possível entrar na sala."));
            }

            var data = await response.Content.ReadFromJsonAsync<LobbyDetalheSalaResponse>();
            return data is null
                ? (false, null, "Resposta inválida ao entrar na sala.")
                : (true, data, null);
        }

        public async Task<(bool Success, LobbyDetalheSalaResponse? Data, string? ErrorMessage)> ObterSalaAsync(int salaId)
        {
            using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, $"api/lobby/salas/{salaId}");
            if (request is null)
            {
                return (false, null, "Sessão inválida.");
            }

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return (false, null, await ReadErrorAsync(response, "Não foi possível carregar a sala."));
            }

            var data = await response.Content.ReadFromJsonAsync<LobbyDetalheSalaResponse>();
            return data is null
                ? (false, null, "Resposta inválida ao carregar a sala.")
                : (true, data, null);
        }

        public async Task<(bool Success, LobbyDetalheSalaResponse? Data, string? ErrorMessage)> SelecionarJogoAsync(int salaId, string gameType)
        {
            using var request = await CreateAuthenticatedRequestAsync(
                HttpMethod.Post,
                $"api/lobby/salas/{salaId}/selecionar-jogo",
                new LobbySelecionarJogoRequest
                {
                    GameType = gameType
                });

            if (request is null)
            {
                return (false, null, "Sessão inválida.");
            }

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return (false, null, await ReadErrorAsync(response, "Não foi possível selecionar o jogo."));
            }

            var data = await response.Content.ReadFromJsonAsync<LobbyDetalheSalaResponse>();
            return data is null
                ? (false, null, "Resposta inválida ao selecionar o jogo.")
                : (true, data, null);
        }

        public async Task<(bool Success, LobbyDetalheSalaResponse? Data, string? ErrorMessage)> AlterarProntoAsync(int salaId, bool isReady)
        {
            using var request = await CreateAuthenticatedRequestAsync(
                HttpMethod.Post,
                $"api/lobby/salas/{salaId}/pronto",
                new LobbyAlterarProntoRequest
                {
                    IsReady = isReady
                });

            if (request is null)
            {
                return (false, null, "Sessão inválida.");
            }

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return (false, null, await ReadErrorAsync(response, "Não foi possível atualizar o status de pronto."));
            }

            var data = await response.Content.ReadFromJsonAsync<LobbyDetalheSalaResponse>();
            return data is null
                ? (false, null, "Resposta inválida ao atualizar o status de pronto.")
                : (true, data, null);
        }

        public async Task<(bool Success, LobbyIniciarJogoResponse? Data, string? ErrorMessage)> IniciarJogoAsync(int salaId)
        {
            using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, $"api/lobby/salas/{salaId}/iniciar");
            if (request is null)
            {
                return (false, null, "Sessão inválida.");
            }

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return (false, null, await ReadErrorAsync(response, "Não foi possível iniciar o jogo."));
            }

            var data = await response.Content.ReadFromJsonAsync<LobbyIniciarJogoResponse>();
            return data is null
                ? (false, null, "Resposta inválida ao iniciar o jogo.")
                : (true, data, null);
        }

        public async Task<(bool Success, GameSessionResponse? Data, string? ErrorMessage)> ObterSessaoJogoAsync(int salaId)
        {
            using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, $"api/jogo/{salaId}");
            if (request is null)
            {
                return (false, null, "Sessão inválida.");
            }

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return (false, null, await ReadErrorAsync(response, "Não foi possível carregar a sessão do jogo."));
            }

            var data = await response.Content.ReadFromJsonAsync<GameSessionResponse>();
            return data is null
                ? (false, null, "Resposta inválida ao carregar a sessão do jogo.")
                : (true, data, null);
        }

        public async Task<(bool Success, LobbySairSalaResponse? Data, string? ErrorMessage)> SairSalaAsync(int salaId)
        {
            using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, $"api/lobby/salas/{salaId}/sair");
            if (request is null)
            {
                return (false, null, "Sessão inválida.");
            }

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return (false, null, await ReadErrorAsync(response, "Não foi possível sair da sala."));
            }

            var data = await response.Content.ReadFromJsonAsync<LobbySairSalaResponse>();
            return data is null
                ? (false, null, "Resposta inválida ao sair da sala.")
                : (true, data, null);
        }

        private async Task<HttpRequestMessage?> CreateAuthenticatedRequestAsync(HttpMethod method, string url, object? body = null)
        {
            var token = await EnsureSessionAsync();
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            if (body is not null)
            {
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            }

            return request;
        }

        private static async Task<string> ReadErrorAsync(HttpResponseMessage response, string fallbackMessage)
        {
            var error = await response.Content.ReadAsStringAsync();
            return string.IsNullOrWhiteSpace(error) ? fallbackMessage : error;
        }
    }
}
