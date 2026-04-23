using Ecomads.WebApplication.Data;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services;

public class StatisticsWorker : BackgroundService
{
    private readonly IStatisticsQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StatisticsWorker> _logger;

    public StatisticsWorker(IStatisticsQueue queue, IServiceProvider serviceProvider, ILogger<StatisticsWorker> logger)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var job = _queue.Dequeue(stoppingToken);
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
                
                _logger.LogInformation("Processing statistics for campaign {CampaignId}", job.CampaignId);
                // Здесь будет логика формирования статистики
                await Task.Delay(1000, stoppingToken); 
                _logger.LogInformation("Statistics for campaign {CampaignId} processed", job.CampaignId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing statistics job");
            }
        }
    }
}
