using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MeuCatan.MudblazorWasmClient;
using MudBlazor.Services;
using MeuCatan.MudblazorWasmClient.Services;
using Microsoft.AspNetCore.Components.Authorization;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<ClienteAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<ClienteAuthStateProvider>());
builder.Services.AddScoped<ClienteAuthService>();
builder.Services.AddMudServices();

var apiBaseUrl = builder.Configuration["ApiBaseUrl"];
var httpBaseAddress = string.IsNullOrWhiteSpace(apiBaseUrl)
    ? builder.HostEnvironment.BaseAddress
    : apiBaseUrl;

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(httpBaseAddress) });

await builder.Build().RunAsync();
