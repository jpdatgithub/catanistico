namespace MeuCatan.Api.Services;

public sealed class LobbyPresenceMaintenanceHostedService : BackgroundService
{
    private const int DefaultMaintenanceIntervalSeconds = 60;

    private readonly ILobbyRoomService _lobbyRoomService;
    private readonly TimeSpan _maintenanceInterval;

    public LobbyPresenceMaintenanceHostedService(ILobbyRoomService lobbyRoomService, IConfiguration configuration)
    {
        _lobbyRoomService = lobbyRoomService;

        var configuredSeconds = configuration.GetValue<int?>("LobbyPresence:MaintenanceIntervalSeconds");
        var maintenanceIntervalSeconds = configuredSeconds.GetValueOrDefault(DefaultMaintenanceIntervalSeconds);
        if (maintenanceIntervalSeconds <= 0)
        {
            maintenanceIntervalSeconds = DefaultMaintenanceIntervalSeconds;
        }

        _maintenanceInterval = TimeSpan.FromSeconds(maintenanceIntervalSeconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_maintenanceInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                _lobbyRoomService.ExecutarManutencaoPresenca();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
