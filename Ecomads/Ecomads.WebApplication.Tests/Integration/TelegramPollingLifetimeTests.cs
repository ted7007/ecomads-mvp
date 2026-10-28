using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Services.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class TelegramPollingLifetimeTests(PostgresFixture postgres)
{
    [Fact]
    public async Task PollTimeoutKeepsWorkerAliveAndPreservesUpdateCursor()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        await using var services = new ServiceCollection()
            .AddScoped(_ => postgres.CreateDbContext(db.Database.GetConnectionString()!)).BuildServiceProvider();
        var logger = new PollLogger();
        using var worker = new TelegramUpdatesWorker(services.GetRequiredService<IServiceScopeFactory>(), new TimeoutBot(), logger);
        await worker.StartAsync(default);
        await logger.Warning.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(worker.ExecuteTask!.IsCompleted);
        Assert.Equal(0, (await db.TelegramBotStates.AsNoTracking().SingleAsync()).NextUpdateId);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StopAsync(stop.Token);
        Assert.False(worker.ExecuteTask.IsFaulted);
    }

    private sealed class TimeoutBot : ITelegramBotClient
    {
        public bool IsConfigured => true;
        public string? BotUsername => null;
        public Task<IReadOnlyList<TelegramIncoming>> GetUpdatesAsync(long offset, CancellationToken ct) =>
            Task.FromException<IReadOnlyList<TelegramIncoming>>(new TaskCanceledException("HTTP polling timeout"));
        public Task SendMessageAsync(long chatId, string text, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class PollLogger : ILogger<TelegramUpdatesWorker>
    {
        public TaskCompletionSource Warning { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> format)
        { if (level == LogLevel.Warning) Warning.TrySetResult(); }
    }
}
