namespace MeuCatan.Api.Services;

public sealed class GameTimerHostedService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);
    private readonly IGameSessionService _gameSessionService;

    public GameTimerHostedService(IGameSessionService gameSessionService)
    {
        _gameSessionService = gameSessionService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                _gameSessionService.ProcessExpiredTimers();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}