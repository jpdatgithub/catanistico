using Microsoft.JSInterop;

namespace MeuCatan.MudblazorWasmClient.Services;

public sealed class AudioCueService(IJSRuntime jsRuntime)
{
    public ValueTask PlayAsync(string cueName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cueName);
        return jsRuntime.InvokeVoidAsync("audioCues.play", cueName);
    }
}