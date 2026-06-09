using System.Diagnostics;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Polyglot.Infrastructure.Services
{
    public class PostgresBackupService(IConfiguration configuration, ILogger<PostgresBackupService> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!configuration.GetValue("Backup:Enabled", false))
            {
                logger.LogInformation("Postgres backups disabled (Backup:Enabled=false).");
                return;
            }

            logger.LogInformation("Postgres Backup Service running.");

            if (configuration.GetValue("Backup:RunOnStartup", false))
                await BackupAsync(stoppingToken);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(DelayUntilNextRun(), stoppingToken);
                    await BackupAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Postgres Backup Service is stopping.");
            }
        }

        private TimeSpan DelayUntilNextRun()
        {
            var timeOfDay = configuration.GetValue("Backup:TimeOfDayUtc", new TimeSpan(3, 0, 0));
            var now = DateTime.UtcNow;
            var next = now.Date + timeOfDay;
            if (next <= now)
                next = next.AddDays(1);
            return next - now;
        }

        private async Task BackupAsync(CancellationToken cancellationToken)
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"polyglot-backup-{Guid.NewGuid():N}.dump");
            try
            {
                var connectionString = configuration.GetConnectionString("PolyglotDatabase")
                    ?? throw new InvalidOperationException("ConnectionStrings:PolyglotDatabase not configured");
                var conn = new NpgsqlConnectionStringBuilder(connectionString);

                await RunPgDumpAsync(conn, tempFile, cancellationToken);

                using var s3 = CreateS3Client();
                var bucket = configuration["Backup:S3:Bucket"] ?? "polyglot-backups";
                await EnsureBucketAsync(s3, bucket, cancellationToken);

                var key = $"postgres/{conn.Database}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.dump";
                await s3.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = bucket,
                    Key = key,
                    FilePath = tempFile,
                }, cancellationToken);

                var size = new FileInfo(tempFile).Length;
                logger.LogInformation("Postgres backup uploaded: {Key} ({Size} bytes)", key, size);

                await PruneAsync(s3, bucket, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Postgres backup failed");
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        private async Task RunPgDumpAsync(NpgsqlConnectionStringBuilder conn, string outputFile, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "pg_dump",
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("--format=custom");
            startInfo.ArgumentList.Add($"--host={conn.Host}");
            startInfo.ArgumentList.Add($"--port={conn.Port}");
            startInfo.ArgumentList.Add($"--username={conn.Username}");
            startInfo.ArgumentList.Add($"--dbname={conn.Database}");
            startInfo.ArgumentList.Add($"--file={outputFile}");
            startInfo.EnvironmentVariables["PGPASSWORD"] = conn.Password;

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start pg_dump");
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"pg_dump exited with code {process.ExitCode}: {stderr}");
        }

        private AmazonS3Client CreateS3Client()
        {
            var endpoint = configuration["Backup:S3:Endpoint"]
                ?? throw new InvalidOperationException("Backup:S3:Endpoint not configured");
            var accessKey = configuration["Backup:S3:AccessKey"]
                ?? throw new InvalidOperationException("Backup:S3:AccessKey not configured");
            var secretKey = configuration["Backup:S3:SecretKey"]
                ?? throw new InvalidOperationException("Backup:S3:SecretKey not configured");

            return new AmazonS3Client(accessKey, secretKey, new AmazonS3Config
            {
                ServiceURL = endpoint,
                ForcePathStyle = true,
            });
        }

        private static async Task EnsureBucketAsync(AmazonS3Client s3, string bucket, CancellationToken cancellationToken)
        {
            try
            {
                await s3.PutBucketAsync(bucket, cancellationToken);
            }
            catch (AmazonS3Exception ex) when (ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
            {
            }
        }

        private async Task PruneAsync(AmazonS3Client s3, string bucket, CancellationToken cancellationToken)
        {
            var retention = configuration.GetValue("Backup:RetentionCount", 14);
            var response = await s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = bucket,
                Prefix = "postgres/",
            }, cancellationToken);

            var keys = response.S3Objects?.Select(o => o.Key) ?? [];
            foreach (var key in SelectKeysToPrune(keys, retention))
            {
                await s3.DeleteObjectAsync(bucket, key, cancellationToken);
                logger.LogInformation("Pruned old backup: {Key}", key);
            }
        }

        // Timestamped keys sort chronologically, so ordering by key descending
        // keeps the newest `retentionCount` backups.
        public static List<string> SelectKeysToPrune(IEnumerable<string> keys, int retentionCount)
        {
            return keys
                .OrderByDescending(k => k, StringComparer.Ordinal)
                .Skip(Math.Max(0, retentionCount))
                .ToList();
        }
    }
}
