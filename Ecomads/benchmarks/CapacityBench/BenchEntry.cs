using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Recommendations;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CapacityBench;

internal static class BenchEntry
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static string measurementConnection = "";
    private sealed record Tenant(Guid Seller, Guid Store, Guid[] Campaigns);
    public static async Task Main(string[] args)
    {
        var stores = args.Length > 0 ? int.Parse(args[0]) : 10;
        if (stores is not (1 or 10 or 50 or 100)) throw new ArgumentException("Use 1, 10, 50 or 100 stores.");
        var output = Path.GetFullPath(args.Length > 1 ? args[1] : $"artifacts/capacity/local-{stores}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var admin = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ECOMADS_CAPACITY_POSTGRES") ??
            "Host=127.0.0.1;Port=55432;Username=capacity;Password=capacity-local-only;Database=capacity_admin;Maximum Pool Size=30");
        // Never accept a production or remote connection, even via an environment override.
        var localHost = admin.Host is "localhost" or "127.0.0.1" ||
            (OperatingSystem.IsLinux() && admin.Host == "host.docker.internal");
        if (!localHost || admin.Port != 55432 ||
            admin.Username != "capacity" || admin.Database != "capacity_admin")
            throw new InvalidOperationException("Only the isolated local capacity_admin database on port 55432 is allowed.");
        var database = $"ecomads_capacity_{stores}_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync();
            await new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection).ExecuteNonQueryAsync();
        }
        var cs = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = database }.ConnectionString;
        measurementConnection = cs;
        Console.WriteLine($"START stores={stores}, database={database}");
        var phases = new List<object>();
        object? report = null;
        try
        {
            await using var factory = new Factory(cs);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            factory.Errors.Messages.Clear(); // A first migration probes a not-yet-existing history table.
            client.Timeout = TimeSpan.FromMinutes(5);
            var tenants = new List<Tenant>();
            await Measure("seed_30_days", async () => tenants = await Seed(factory.Services, stores), phases);
            long seededBytes = await DatabaseBytes(cs);
            // Warm JIT/model compilation separately; use a real non-empty recommendation response.
            await Request(client, tenants[0], "campaign");
            await Measure("fullstats_import_7_days_one_store", async () =>
            {
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
                var campaigns = await db.Campaigns.Where(x => x.StoreId == tenants[0].Store).OrderBy(x => x.WbCampaignId).ToArrayAsync();
                var end = RecommendationPeriods.Economic(DateTime.UtcNow).EndDate;
                using var payload = JsonSerializer.SerializeToDocument(campaigns.Select((campaign, index) => new
                {
                    advertId = long.Parse(campaign.WbCampaignId),
                    days = Enumerable.Range(0, 7).Select(day => new
                    {
                        date = end.AddDays(-day).ToString("yyyy-MM-dd"), sum = 600m, sum_price = 6000m,
                        views = 6000, clicks = 60, atbs = 6, orders = 6, canceled = 0,
                        apps = new[] { new { appType = 1, nms = Enumerable.Range(0, 2).Select(product => new
                        {
                            nmId = 100000 + index * 2 + product, name = "Synthetic product", sum = 300m,
                            sum_price = 3000m, views = 3000, clicks = 30, atbs = 3, orders = 3, canceled = 0, shks = 3
                        }) } }
                    })
                }));
                var result = await scope.ServiceProvider.GetRequiredService<WbFullStatsImporter>().ImportAsync(
                    tenants[0].Store, payload.RootElement, default, campaigns.Select(x => long.Parse(x.WbCampaignId)).ToArray(),
                    requestedStart: end.AddDays(-6), requestedEnd: end);
                if (result.Rows == 0 || result.MissingCampaignIds.Length != 0) throw new InvalidOperationException("Synthetic import did not apply.");
            }, phases);
            var concurrency = stores >= 100 ? 10 : stores >= 50 ? 5 : Math.Min(stores, 3);
            var latencies = new ConcurrentBag<RequestResult>();
            double backgroundSeconds = 0;
            // Each virtual user completes ten page cycles; every client gets a unique seller header per request.
            await Measure("http_with_background_calculation", async () =>
            {
                var background = RunBackground(factory.Services);
                await Parallel.ForEachAsync(Enumerable.Range(0, concurrency), new ParallelOptions { MaxDegreeOfParallelism = concurrency },
                    async (user, ct) =>
                    {
                        for (var cycle = 0; cycle < 10; cycle++)
                        {
                            var tenant = tenants[(user + cycle * concurrency) % tenants.Count];
                            foreach (var route in new[] { "projects", "campaign", "summary" })
                                latencies.Add(await Request(client, tenant, route));
                            await Task.Delay(250, ct);
                        }
                    });
                backgroundSeconds = await background;
            }, phases);
            var routes = latencies.GroupBy(x => x.Route).Select(group => new
            {
                route = group.Key, requests = group.Count(), errors = group.Count(x => x.Status != 200),
                p50Ms = Percentile(group.Select(x => x.Milliseconds), .50),
                p95Ms = Percentile(group.Select(x => x.Milliseconds), .95),
                maxMs = group.Max(x => x.Milliseconds), responseBytes = group.Sum(x => x.Bytes)
            }).ToArray();
            if (routes.Sum(x => x.errors) != 0) throw new InvalidOperationException("HTTP errors: " + JsonSerializer.Serialize(routes));
            using var validationScope = factory.Services.CreateScope();
            var validationDb = validationScope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
            var calculatedCampaigns = await validationDb.WbRecommendationRuns.Select(x => x.CampaignId).Distinct().CountAsync();
            var backgroundErrors = factory.Errors.Messages.Where(x => x.StartsWith(typeof(RecommendationWorker).FullName! + ":")).ToArray();
            if (calculatedCampaigns != stores * 40 || factory.Outbound.Attempts != 0 || backgroundErrors.Length != 0)
                throw new InvalidOperationException($"Invalid benchmark: campaigns={calculatedCampaigns}, outbound={factory.Outbound.Attempts}, errors={JsonSerializer.Serialize(factory.Errors.Messages)}");
            report = new
            {
                measuredAtUtc = DateTime.UtcNow, stores, campaigns = stores * 40, catalogProducts = stores * 100,
                campaignProductPairs = stores * 80, clustersPerPair = 3, days = 30, concurrency,
                hostLogicalProcessors = Environment.ProcessorCount, runtime = Environment.Version.ToString(),
                applicationLimits = Environment.GetEnvironmentVariable("ECOMADS_CAPACITY_APP_LIMITS") ?? "Windows host; no CPU/RAM cap",
                databaseLimits = Environment.GetEnvironmentVariable("ECOMADS_CAPACITY_DB_LIMITS") ?? "1 CPU / 512 MiB",
                seededDatabaseBytes = seededBytes, databaseBytes = await DatabaseBytes(cs), database,
                phases, routes, backgroundSeconds, calculatedCampaigns, factory.Outbound.Attempts,
                backgroundErrorCount = backgroundErrors.Length,
                databaseErrorLogEntries = factory.Errors.Messages.Count,
                limitations = new[] { Environment.GetEnvironmentVariable("ECOMADS_CAPACITY_APP_LIMITS") == null
                    ? "Application runs on the Windows host, not under a VPS CPU/RAM limit."
                    : "Application has Docker CPU/RAM limits; Docker Desktop performance is not VPS performance.",
                    "PostgreSQL container memory includes page cache; report application process memory excludes PostgreSQL.",
                    "Synthetic 30-day data; no raw-response archive, historical decisions or year-long retention seeded.",
                    "Real recommendation worker is invoked once with external refresh disabled; WB requests are blocked.",
                    "HTTP uses ASP.NET TestServer, excludes TLS, reverse proxy, browser rendering and network latency.",
                    "One real fullstats importer measured; other importers and API quota timing require separate modelling.",
                    "EF can log a unique-key conflict handled by RecommendationService during concurrent identical calculations; HTTP/background results are validated separately." }
            };
            Console.WriteLine(JsonSerializer.Serialize(report, Json));
        }
        catch (Exception error)
        {
            report = new { stores, database, phases, error = error.ToString(), measuredAtUtc = DateTime.UtcNow };
            throw;
        }
        finally
        {
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, Json));
            NpgsqlConnection.ClearAllPools();
            // Drop only the exact random database created above, in the dedicated local container.
            await using var connection = new NpgsqlConnection(admin.ConnectionString);
            await connection.OpenAsync();
            await new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", connection).ExecuteNonQueryAsync();
        }
    }

    private static async Task<double> RunBackground(IServiceProvider services)
    {
        var started = Stopwatch.StartNew();
        var options = services.GetRequiredService<IOptions<RecommendationOptions>>();
        var worker = new RecommendationWorker(services.GetRequiredService<IServiceScopeFactory>(), options,
            services.GetRequiredService<ILogger<RecommendationWorker>>());
        await worker.RunOnceAsync(DateTime.UtcNow, default);
        Console.WriteLine($"BACKGROUND elapsed={started.Elapsed.TotalSeconds:F2}s");
        return started.Elapsed.TotalSeconds;
    }

    private static async Task Measure(string name, Func<Task> action, List<object> phases)
    {
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        var allocated = GC.GetTotalAllocatedBytes();
        var databaseBefore = await DatabaseResources();
        var timer = Stopwatch.StartNew();
        Console.WriteLine($"PHASE {name}");
        await action();
        timer.Stop(); process.Refresh();
        var cpuSeconds = (process.TotalProcessorTime - cpu).TotalSeconds;
        var databaseAfter = await DatabaseResources();
        phases.Add(new { name, elapsedSeconds = timer.Elapsed.TotalSeconds, processCpuSeconds = cpuSeconds,
            processCpuCores = cpuSeconds / Math.Max(.001, timer.Elapsed.TotalSeconds),
            databaseCpuSeconds = databaseAfter.CpuSeconds - databaseBefore.CpuSeconds,
            databaseMemoryBytes = databaseAfter.MemoryBytes, databasePeakMemoryBytes = databaseAfter.PeakMemoryBytes,
            allocatedBytes = GC.GetTotalAllocatedBytes() - allocated, workingSetBytes = process.WorkingSet64,
            peakWorkingSetBytes = process.PeakWorkingSet64, managedHeapBytes = GC.GetTotalMemory(false) });
        Console.WriteLine($"DONE {name} elapsed={timer.Elapsed.TotalSeconds:F2}s appCPU={cpuSeconds:F2}s rss={process.WorkingSet64 / 1048576}MiB");
    }

    private sealed record RequestResult(string Route, int Status, double Milliseconds, int Bytes);
    private static async Task<RequestResult> Request(HttpClient client, Tenant tenant, string route)
    {
        var path = route switch { "projects" => "/api/projects", "campaign" => $"/api/wb/campaigns/{tenant.Campaigns[0]}/recommendations",
            _ => $"/api/wb/recommendations/summary?storeId={tenant.Store}" };
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Capacity-Seller", tenant.Seller.ToString());
        var timer = Stopwatch.StartNew();
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsByteArrayAsync();
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"{path} returned {(int)response.StatusCode}: {System.Text.Encoding.UTF8.GetString(body)[..Math.Min(500, body.Length)]}");
        if (route == "campaign")
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.GetProperty("evaluations").GetArrayLength() < 6)
                throw new InvalidOperationException("Recommendation benchmark unexpectedly has empty evaluations.");
        }
        return new(route, (int)response.StatusCode, timer.Elapsed.TotalMilliseconds, body.Length);
    }

    private static double Percentile(IEnumerable<double> source, double quantile)
    { var sorted = source.Order().ToArray(); return sorted[(int)Math.Ceiling(sorted.Length * quantile) - 1]; }

    private sealed record DatabaseResource(double CpuSeconds, long MemoryBytes, long PeakMemoryBytes);
    private static async Task<DatabaseResource> DatabaseResources()
    {
        async Task<string?> Read(params string[] paths)
        {
            // The dedicated test DB role is a superuser; inspect its own cgroup without a Docker socket in the app.
            await using var connection = new NpgsqlConnection(measurementConnection);
            await connection.OpenAsync();
            var contents = new List<string>();
            foreach (var path in paths)
            {
                await using var command = new NpgsqlCommand("SELECT pg_read_file(@path,0,4096,true)", connection);
                command.Parameters.AddWithValue("path", path);
                var value = await command.ExecuteScalarAsync();
                if (value is not string content) return null;
                contents.Add(content.TrimEnd());
            }
            return string.Join('\n', contents);
        }
        var output = await Read("/sys/fs/cgroup/cpu.stat", "/sys/fs/cgroup/memory.current", "/sys/fs/cgroup/memory.peak");
        if (output == null)
        {
            output = await Read("/sys/fs/cgroup/cpuacct/cpuacct.usage", "/sys/fs/cgroup/memory/memory.usage_in_bytes", "/sys/fs/cgroup/memory/memory.max_usage_in_bytes")
                ?? throw new InvalidOperationException("Cannot sample isolated PostgreSQL cgroup v1 or v2.");
            var values = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(long.Parse).ToArray();
            return new(values[0] / 1e9, values[1], values[2]);
        }
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return new(double.Parse(lines.Single(x => x.StartsWith("usage_usec ")).Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture) / 1e6,
            long.Parse(lines[^2]), long.Parse(lines[^1]));
    }

    private static async Task<long> DatabaseBytes(string cs)
    {
        await using var connection = new NpgsqlConnection(cs); await connection.OpenAsync();
        return (long)(await new NpgsqlCommand("SELECT pg_database_size(current_database())", connection).ExecuteScalarAsync())!;
    }

    private static async Task<List<Tenant>> Seed(IServiceProvider services, int count)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
        var tenants = new List<Tenant>();
        for (var storeNumber = 0; storeNumber < count; storeNumber++)
        {
            var seller = new Seller { Id = Guid.NewGuid(), Name = "Capacity synthetic", Email = $"capacity-{Guid.NewGuid():N}@example.test", PasswordHash = "not-a-login" };
            var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "Capacity synthetic", ApiKey = "synthetic-no-network", AutoRefreshEnabled = false };
            var campaigns = Enumerable.Range(0, 40).Select(index => new Campaign { Id = Guid.NewGuid(), StoreId = store.Id,
                WbCampaignId = (1000 + index).ToString(), Name = $"Campaign {index}", WbStatus = 9 }).ToArray();
            var products = Enumerable.Range(0, 100).Select(index => new Nomenclature { Id = Guid.NewGuid(), StoreId = store.Id,
                WbNomenclatureId = (100000 + index).ToString(), Name = $"Product {index}" }).ToArray();
            db.Sellers.Add(seller); db.Stores.Add(store); db.Campaigns.AddRange(campaigns); db.Nomenclatures.AddRange(products);
            tenants.Add(new(seller.Id, store.Id, campaigns.Select(x => x.Id).ToArray()));
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        }
        var end = RecommendationPeriods.Economic(DateTime.UtcNow).EndDate;
        // Metadata uses EF; large deterministic data uses set-based SQL, so seeding does not dominate the measurement.
        var sql = $$"""
            CREATE TEMP TABLE capacity_pairs AS SELECT c.id AS campaign, n.id AS article, c.store_id AS store
            FROM campaigns c JOIN nomenclatures n ON n.store_id=c.store_id
            AND n.wb_nomenclature_id::int BETWEEN 100000+(c.wb_campaign_id::int-1000)*2 AND 100001+(c.wb_campaign_id::int-1000)*2;
            CREATE TEMP TABLE capacity_dates AS SELECT DATE '{{end:yyyy-MM-dd}}' - day AS date FROM generate_series(0,29) AS day;
            INSERT INTO campaign_statistics (campaign_id,date,revenue,spend,clicks,ctr,drr,impressions,carts,orders,cancellations)
            SELECT c.id,d.date::timestamptz,6000,600,60,1,10,6000,6,6,0 FROM campaigns c CROSS JOIN capacity_dates d;
            INSERT INTO campaign_nomenclature_statistics
            (campaign_id,nomenclature_id,date,spend,revenue,impressions,clicks,carts,orders,ordered_products,recommendation_fields_verified,cancellations)
            SELECT p.campaign,p.article,d.date::timestamptz,300,3000,3000,30,3,3,3,true,0 FROM capacity_pairs p CROSS JOIN capacity_dates d;
            INSERT INTO wb_cluster_statistics
            (id,campaign_id,nomenclature_id,date,cluster_name,spend,views,clicks,carts,orders,ordered_products)
            SELECT md5(p.campaign::text||p.article::text||d.date::text||cl::text)::uuid,
              p.campaign,p.article,d.date,'query '||cl,100,1000,10,1,1,CASE WHEN cl=1 THEN 0 ELSE 1 END
            FROM capacity_pairs p CROSS JOIN capacity_dates d CROSS JOIN generate_series(1,3) cl;
            INSERT INTO wb_campaign_daily_checks
            (campaign_id,date,result,spend,checked_at_utc,last_attempt_at_utc,last_attempt_result)
            SELECT c.id,d.date,'data',600,now(),now(),'data' FROM campaigns c CROSS JOIN capacity_dates d;
            INSERT INTO wb_source_quality ("StoreId","Source","Entity","StartDate","EndDate","State","LastAttemptAtUtc","AcceptedAtUtc")
            SELECT p.store,'clusters',p.campaign::text||':'||p.article::text,d.date,d.date,'data',now(),now()
            FROM capacity_pairs p CROSS JOIN capacity_dates d;
            INSERT INTO wb_source_quality ("StoreId","Source","Entity","StartDate","EndDate","State","LastAttemptAtUtc","AcceptedAtUtc")
            SELECT c.store_id,'fullstats',c.id::text,d.date,d.date,'data',now(),now() FROM campaigns c CROSS JOIN capacity_dates d;
            ANALYZE;
            """;
        db.Database.SetCommandTimeout(180);
        await db.Database.ExecuteSqlRawAsync(sql);
        return tenants;
    }

    private sealed class Factory(string connection) : WebApplicationFactory<global::Program>
    {
        public OutboundBlocker Outbound { get; } = new();
        public ErrorLogger Errors { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseContentRoot(Path.Combine(Directory.GetCurrentDirectory(), "Ecomads.WebApplication"));
            builder.ConfigureLogging(logging => { logging.ClearProviders(); logging.AddProvider(Errors); });
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connection,
                ["JwtSettings:SecretKey"] = "capacity-local-only-signing-key-at-least-32-characters",
                ["Wb:AutoRefresh:Enabled"] = "false", ["Telegram:BotToken"] = "",
                ["Wb:Recommendations:Enabled"] = "true", ["Wb:Recommendations:ResultsEnabled"] = "true"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddSingleton<IHttpMessageHandlerBuilderFilter>(Outbound);
                services.AddAuthentication(options => { options.DefaultAuthenticateScheme = "Capacity"; options.DefaultChallengeScheme = "Capacity"; })
                    .AddScheme<AuthenticationSchemeOptions, CapacityAuth>("Capacity", _ => { });
            });
        }
    }

    private sealed class ErrorLogger : ILoggerProvider
    {
        public ConcurrentBag<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => new Capture(this, categoryName);
        public void Dispose() { }
        private sealed class Capture(ErrorLogger owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            { if (IsEnabled(level)) owner.Messages.Add(category + ": " + formatter(state, exception) + " " + exception); }
        }
    }

    private sealed class CapacityAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Capacity-Seller", out var seller)) return Task.FromResult(AuthenticateResult.NoResult());
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, seller.ToString())], Scheme.Name)), Scheme.Name)));
        }
    }

    private sealed class OutboundBlocker : IHttpMessageHandlerBuilderFilter
    {
        private int attempts;
        public int Attempts => attempts;
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        { next(builder); builder.PrimaryHandler = new Reject(this); };
        private sealed class Reject(OutboundBlocker owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            { Interlocked.Increment(ref owner.attempts); throw new InvalidOperationException("External HTTP is forbidden in capacity measurements."); }
        }
    }
}
