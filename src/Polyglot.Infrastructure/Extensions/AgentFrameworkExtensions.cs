using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;

namespace Polyglot.Infrastructure.Extensions
{
    public static class AgentFrameworkExtensions
    {
        public static IServiceCollection AddAgentFramework(this IServiceCollection services, IConfiguration configuration)
        {
            var apiKey = configuration["OpenRouter:ApiKey"] ?? throw new InvalidOperationException("OpenRouter:ApiKey not configured");

            var options = new OpenAIClientOptions
            {
                Endpoint = new Uri("https://openrouter.ai/api/v1")
            };

            var openAiClient = new OpenAIClient(new ApiKeyCredential(apiKey), options);
            services.AddSingleton(openAiClient);
            services.AddSingleton<IChatClientFactory, OpenAIChatClientFactory>();

            return services;
        }
    }

    public interface IChatClientFactory
    {
        IChatClient Create(string modelId);
    }

    internal sealed class OpenAIChatClientFactory(OpenAIClient openAiClient) : IChatClientFactory
    {
        public IChatClient Create(string modelId) => openAiClient.GetChatClient(modelId).AsIChatClient();
    }
}
