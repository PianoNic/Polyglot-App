using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polyglot.Infrastructure.Extensions;

namespace Polyglot.Infrastructure.Services;

public class ChatTitleGenerator(
    IServiceScopeFactory scopeFactory,
    IChatClientFactory chatClientFactory,
    IConfiguration configuration,
    ILogger<ChatTitleGenerator> logger) : IChatTitleGenerator
{
    private const string DefaultModel = "mistralai/mistral-nemo";
    private const int DefaultInputCapTokens = 200;
    private const string SystemPrompt = "Generate a concise 3-6 word title summarizing this conversation. Output only the title, no quotes, no punctuation, no prefix.";

    public async Task GenerateAndSaveAsync(Guid chatId, string userMessage, string assistantMessage, string placeholderTitle)
    {
        try
        {
            var model = configuration["Chat:TitleModel"] ?? DefaultModel;
            var inputCapTokens = configuration.GetValue("Chat:TitleInputCapTokens", DefaultInputCapTokens);
            var charBudgetPerSide = inputCapTokens * 4 / 2;

            var prompt = $"User: {Truncate(userMessage, charBudgetPerSide)}\nAssistant: {Truncate(assistantMessage, charBudgetPerSide)}";

            var chatClient = chatClientFactory.Create(model);
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, SystemPrompt),
                new(ChatRole.User, prompt),
            };
            var response = await chatClient.GetResponseAsync(messages);
            var title = response.Text?.Trim().Trim('"').Trim('\'').TrimEnd('.', '!', '?').Trim();
            if (string.IsNullOrWhiteSpace(title))
                return;

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PolyglotDbContext>();
            var chat = await db.Chats.SingleOrDefaultAsync(c => c.Id == chatId);
            if (chat is null || chat.Title != placeholderTitle)
                return;

            chat.Title = title.Length > 200 ? title[..200] : title;
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to generate chat title for chat {ChatId}", chatId);
        }
    }

    private static string Truncate(string value, int maxChars)
    {
        return value.Length <= maxChars ? value : value[..maxChars];
    }
}
