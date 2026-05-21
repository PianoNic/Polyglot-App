using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Polyglot.Application.Dtos;
using Polyglot.Application.Mappers;
using Polyglot.Application.Models;
using Polyglot.Domain;
using Polyglot.Domain.Enums;
using Polyglot.Infrastructure;
using Polyglot.Infrastructure.Services;

namespace Polyglot.Application.Command
{
    public record SendMessageCommand(Guid? ChatId, string Message, string? Model) : ICommand<Result<SendMessageDto>>;

    public class SendMessageCommandHandler(IUserService userService, PolyglotDbContext dbContext, IChatCompletionServiceFactory chatCompletionFactory, ICreditsService creditsService) : ICommandHandler<SendMessageCommand, Result<SendMessageDto>>
    {
        public async ValueTask<Result<SendMessageDto>> Handle(SendMessageCommand command, CancellationToken cancellationToken)
        {
            Console.WriteLine($"[SendMessageHandler] Invoked: chatId={command.ChatId}, model={command.Model}, msg={command.Message?[..Math.Min(30, command.Message?.Length ?? 0)]}");

            if (string.IsNullOrEmpty(command.Model))
                return Result<SendMessageDto>.Failure("Model is required");

            var userId = await userService.GetCurrentUserIdAsync(cancellationToken);
            var user = await dbContext.Users.SingleAsync(u => u.Id == userId, cancellationToken);

            if (user.IsLocked)
                return Result<SendMessageDto>.Failure("Your account has been locked. Please contact an administrator.");

            var model = await dbContext.Models.SingleOrDefaultAsync(m => m.ModelId == command.Model, cancellationToken);
            if (model is null)
                return Result<SendMessageDto>.Failure($"Model '{command.Model}' not found");

            Chat chat;
            if (command.ChatId is not null)
            {
                var existing = await dbContext.Chats
                    .Include(c => c.Messages.OrderBy(m => m.SequenceNumber))
                    .SingleOrDefaultAsync(c => c.Id == command.ChatId.Value && c.UserId == userId, cancellationToken);
                if (existing is null)
                    return Result<SendMessageDto>.Failure("Chat not found");
                chat = existing;
            }
            else
            {
                chat = new Chat
                {
                    UserId = userId,
                    Title = "New Chat",
                    User = null!,
                    Messages = []
                };
                dbContext.Chats.Add(chat);
            }

            var inputCharCount = chat.Messages.Sum(m => m.Content.Length) + command.Message.Length;
            var worstCaseCredits = await creditsService.EstimateChatCreditsAsync(
                inputCharCount,
                model.PromptPricePerMillion,
                model.CompletionPricePerMillion,
                cancellationToken);

            if (user.CreditBalance < worstCaseCredits)
                return Result<SendMessageDto>.Failure($"Insufficient credits (need ~{worstCaseCredits}, have {user.CreditBalance})");

            var nextSequence = chat.Messages.Select(m => m.SequenceNumber).DefaultIfEmpty(-1).Max() + 1;

            var userMessage = new Message
            {
                ChatId = chat.Id,
                Role = MessageRole.User,
                Content = command.Message,
                SequenceNumber = nextSequence
            };
            chat.Messages.Add(userMessage);
            dbContext.Messages.Add(userMessage);

            var history = new ChatHistory();
            foreach (var msg in chat.Messages.OrderBy(m => m.SequenceNumber))
            {
                switch (msg.Role)
                {
                    case MessageRole.User:
                        history.AddUserMessage(msg.Content);
                        break;
                    case MessageRole.Assistant:
                        history.AddAssistantMessage(msg.Content);
                        break;
                    case MessageRole.System:
                        history.AddSystemMessage(msg.Content);
                        break;
                }
            }

            var chatCompletionService = chatCompletionFactory.Create(command.Model);
            var response = await chatCompletionService.GetChatMessageContentAsync(history, cancellationToken: cancellationToken);

            var (promptTokens, completionTokens) = ExtractUsage(response);
            var actualCredits = await creditsService.CalculateChatCreditsAsync(
                promptTokens,
                completionTokens,
                model.PromptPricePerMillion,
                model.CompletionPricePerMillion,
                cancellationToken);

            user.CreditBalance -= actualCredits;

            var assistantMessage = new Message
            {
                ChatId = chat.Id,
                Role = MessageRole.Assistant,
                Content = response.Content ?? string.Empty,
                Model = command.Model,
                FinishReason = response.Metadata?.TryGetValue("FinishReason", out var fr) == true ? fr?.ToString() : null,
                TokenUsage = $"{promptTokens}/{completionTokens}",
                SequenceNumber = nextSequence + 1
            };
            chat.Messages.Add(assistantMessage);
            dbContext.Messages.Add(assistantMessage);

            if (chat.Title == "New Chat" && nextSequence == 0)
            {
                chat.Title = command.Message.Length > 50
                    ? command.Message[..50] + "..."
                    : command.Message;
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            return Result<SendMessageDto>.Success(new SendMessageDto
            {
                ChatId = chat.Id,
                ChatTitle = chat.Title,
                UserMessage = userMessage.ToDto(),
                AssistantMessage = assistantMessage.ToDto(),
            });
        }

        private static (int PromptTokens, int CompletionTokens) ExtractUsage(ChatMessageContent response)
        {
            if (response.Metadata is null)
                return (0, 0);
            if (!response.Metadata.TryGetValue("Usage", out var usageObj) || usageObj is null)
                return (0, 0);

            var type = usageObj.GetType();
            var prompt = TryReadInt(usageObj, type, "InputTokenCount") ?? TryReadInt(usageObj, type, "PromptTokens") ?? 0;
            var completion = TryReadInt(usageObj, type, "OutputTokenCount") ?? TryReadInt(usageObj, type, "CompletionTokens") ?? 0;
            return (prompt, completion);
        }

        private static int? TryReadInt(object obj, Type type, string propertyName)
        {
            var prop = type.GetProperty(propertyName);
            if (prop is null)
                return null;
            return prop.GetValue(obj) switch
            {
                int i => i,
                long l => (int)l,
                _ => null
            };
        }
    }
}
