using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ecomads.WebApplication.Services;

public class StatisticsBackgroundService : BackgroundService
{
    private readonly IStatisticsQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StatisticsBackgroundService> _logger;

    public StatisticsBackgroundService(
        IStatisticsQueue queue, 
        IServiceProvider serviceProvider,
        ILogger<StatisticsBackgroundService> logger)
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

            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();

            var startDateUtc = DateTime.SpecifyKind(job.StartDate, DateTimeKind.Utc);
            var endDateUtc = DateTime.SpecifyKind(job.EndDate, DateTimeKind.Utc);

            var keywordStats = await dbContext.KeywordStatistics
                .Where(ks => ks.CompaignId == job.CampaignId && ks.StartDate == startDateUtc && ks.EndDate == endDateUtc)
                .ToListAsync(stoppingToken);

            // Генерируем рекомендации после получения статистики
            try 
            {
                // Получаем сервис рекомендаций из DI контейнера
                var recommendationService = scope.ServiceProvider.GetRequiredService<IRecommendationService>();
                
                // Задачи для разных целей рекомендаций
                var goals = new[] { "рост прибыли", "увеличение заказов", "оптимизация ДРР" };
                
                foreach (var goal in goals)
                {
                    try
                    {
                        _logger.LogInformation("Генерация рекомендаций для кампании {CampaignId} с целью: {Goal}", job.CampaignId, goal);
                        var recommendation = await recommendationService.GenerateRecommendationAsync(job.CampaignId, goal);
                        _logger.LogInformation("Рекомендация успешно сгенерирована: {RecommendationId}", recommendation?.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка при генерации рекомендации для кампании {CampaignId} с целью: {Goal}", job.CampaignId, goal);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при генерации рекомендаций для кампании {CampaignId}", job.CampaignId);
            }
            
            // Небольшая задержка перед обработкой следующего задания
            await Task.Delay(3000);
        }
    }
}
