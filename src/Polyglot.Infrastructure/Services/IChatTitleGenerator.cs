namespace Polyglot.Infrastructure.Services;

public interface IChatTitleGenerator
{
    Task GenerateAndSaveAsync(Guid chatId, string userMessage, string assistantMessage, string placeholderTitle);
}
