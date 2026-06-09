using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Polyglot.Infrastructure.Services
{
    public class AttachmentCleanupService(IServiceProvider services, ILogger<AttachmentCleanupService> logger) : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
        private static readonly TimeSpan OrphanAge = TimeSpan.FromHours(24);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("Attachment Cleanup Service running.");

            using PeriodicTimer timer = new(Interval);

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    await CleanupAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Attachment Cleanup Service is stopping.");
            }
        }

        private async Task CleanupAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<PolyglotDbContext>();

                var cutoff = DateTime.UtcNow - OrphanAge;
                var deleted = await dbContext.Attachments
                    .Where(a => a.MessageId == null && a.CreatedAt < cutoff)
                    .ExecuteDeleteAsync(cancellationToken);

                if (deleted > 0)
                    logger.LogInformation("Deleted {Count} orphaned attachments", deleted);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Attachment cleanup failed");
            }
        }
    }
}
