using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services;

public class StatisticsBackgroundService : BackgroundService
{
    private readonly IStatisticsQueue _queue;
    private readonly IServiceProvider _serviceProvider;

    public StatisticsBackgroundService(IStatisticsQueue queue, IServiceProvider serviceProvider)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var job = _queue.Dequeue(stoppingToken);

            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();

            var stats = await dbContext.CompaignStatistics
                .FirstOrDefaultAsync(s => s.CompaignId == job.CampaignId && s.StartDate == job.StartDate && s.EndDate == job.EndDate, stoppingToken);
            
            // Здесь должна быть логика формирования статистики
            // ...
            
            await Task.Delay(1000, stoppingToken);
        }
    }
}
