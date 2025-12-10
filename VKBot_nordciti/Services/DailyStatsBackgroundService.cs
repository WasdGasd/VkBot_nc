using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace VKBot_nordciti.Services
{
    public class DailyStatsBackgroundService : BackgroundService
    {
        private readonly IBotStatsService _statsService;
        private readonly ILogger<DailyStatsBackgroundService> _logger;

        public DailyStatsBackgroundService(
            IBotStatsService statsService,
            ILogger<DailyStatsBackgroundService> logger)
        {
            _statsService = statsService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Служба сохранения статистики запущена");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _statsService.SaveDailyStatsAsync();
                    _logger.LogDebug("Ежедневная статистика сохранена");

                    // Ждем 5 минут
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка сохранения статистики");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
        }
    }
}