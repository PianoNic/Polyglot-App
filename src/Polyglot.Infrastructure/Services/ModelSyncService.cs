using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polyglot.Domain;
using Polyglot.Infrastructure.Clients;

namespace Polyglot.Infrastructure.Services
{
    public class ModelSyncService(IServiceProvider services, ILogger<ModelSyncService> logger) : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromHours(12);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("Model Sync Service running.");
            await DoWork(stoppingToken);
        }

        private async Task DoWork(CancellationToken stoppingToken)
        {
            logger.LogInformation("Model Sync Service is working.");

            await SyncAsync(stoppingToken);

            using PeriodicTimer timer = new(Interval);

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    await SyncAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Model Sync Service is stopping.");
            }
        }

        private async Task SyncAsync(CancellationToken cancellationToken)
        {
            try
            {
                using (var scope = services.CreateScope())
                {
                    var openRouter =
                        scope.ServiceProvider
                            .GetRequiredService<IOpenRouterClient>();
                    var dbContext =
                        scope.ServiceProvider
                            .GetRequiredService<PolyglotDbContext>();

                    var fetched = await openRouter.GetModelsAsync(cancellationToken);
                    var existing = await dbContext.Models.ToListAsync(cancellationToken);
                    var existingByModelId = existing.ToDictionary(m => m.ModelId);
                    var fetchedIds = fetched.Select(f => f.Id).ToHashSet();

                    foreach (var f in fetched)
                    {
                        if (existingByModelId.TryGetValue(f.Id, out var row))
                        {
                            row.Name = f.Name;
                            row.ContextLength = f.ContextLength;
                            row.InputModalities = f.InputModalities;
                            row.OutputModalities = f.OutputModalities;
                            row.PromptPricePerMillion = f.InputPricePer1M;
                            row.CompletionPricePerMillion = f.OutputPricePer1M;
                        }
                        else
                        {
                            dbContext.Models.Add(new Model
                            {
                                ModelId = f.Id,
                                Name = f.Name,
                                ContextLength = f.ContextLength,
                                InputModalities = f.InputModalities,
                                OutputModalities = f.OutputModalities,
                                PromptPricePerMillion = f.InputPricePer1M,
                                CompletionPricePerMillion = f.OutputPricePer1M,
                            });
                        }
                    }

                    foreach (var row in existing.Where(m => !fetchedIds.Contains(m.ModelId)))
                    {
                        dbContext.Models.Remove(row);
                    }

                    await dbContext.SaveChangesAsync(cancellationToken);

                    logger.LogInformation("Model sync complete: {Count} models from OpenRouter", fetched.Count);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Model sync failed");
            }
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("Model Sync Service is stopping.");

            await base.StopAsync(stoppingToken);
        }
    }
}