using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace MeuCatan.MudblazorWasmClient.Services
{
    public class ClienteAuthStateProvider : AuthenticationStateProvider
    {
        public const string TokenStorageKey = "cliente_auth_token";

        private readonly IJSRuntime _jsRuntime;

        public ClienteAuthStateProvider(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            try
            {
                var token = await GetTokenAsync();
                if (string.IsNullOrWhiteSpace(token))
                {
                    return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
                }

                var jwtHandler = new JwtSecurityTokenHandler();
                if (!jwtHandler.CanReadToken(token))
                {
                    await ClearTokenAsync();
                    return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
                }

                var jwt = jwtHandler.ReadJwtToken(token);
                if (jwt.ValidTo < DateTime.UtcNow)
                {
                    await ClearTokenAsync();
                    return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
                }

                var identity = new ClaimsIdentity(jwt.Claims, "jwt");
                return new AuthenticationState(new ClaimsPrincipal(identity));
            }
            catch
            {
                await ClearTokenAsync();
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }
        }

        public async Task MarkUserAsAuthenticatedAsync(string token)
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("localStorage.setItem", TokenStorageKey, token);
            }
            catch
            {
            }

            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        }

        public async Task MarkUserAsLoggedOutAsync()
        {
            await ClearTokenAsync();
            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        }

        public Task<string?> GetTokenAsync()
        {
            return _jsRuntime.InvokeAsync<string?>("localStorage.getItem", TokenStorageKey).AsTask();
        }

        public async Task<bool> IsGuestAsync()
        {
            var authState = await GetAuthenticationStateAsync();
            return authState.User.Identity?.IsAuthenticated == true
                && string.Equals(authState.User.FindFirst("cliente_tipo")?.Value, "convidado", StringComparison.OrdinalIgnoreCase);
        }

        private async Task ClearTokenAsync()
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", TokenStorageKey);
            }
            catch
            {
            }
        }
    }
}
