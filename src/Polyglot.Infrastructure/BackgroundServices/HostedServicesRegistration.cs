using Microsoft.Extensions.DependencyInjection;
using Polyglot.Infrastructure.Services;

namespace Polyglot.Infrastructure.BackgroundServices
{
    public static class HostedServicesRegistration
    {
        public static IServiceCollection AddHostedServices(this IServiceCollection services)
        {
            services.AddHostedService<ModelSyncService>();
            services.AddHostedService<PostgresBackupService>();
            return services;
        }
    }
}
