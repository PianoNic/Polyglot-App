using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Polyglot.Application.Dtos;
using Polyglot.Application.Mappers;
using Polyglot.Application.Models;
using Polyglot.Domain;
using Polyglot.Domain.Enums;
using Polyglot.Infrastructure;
using Polyglot.Infrastructure.Extensions;
using Polyglot.Infrastructure.Services;

namespace Polyglot.Application.Command
{
    public record SendMessageCommand(Guid? ChatId, string Message, string? Model) : ICommand<Result<SendMessageDto>>;

    public class SendMessageCommandHandler(IUserService userService, PolyglotDbContext dbContext, IChatClientFactory chatClientFactory, ICreditsService creditsService) : ICommandHandler<SendMessageCommand, Result<SendMessageDto>>
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

            var messages = new List<ChatMessage>(chat.Messages.Count);
            foreach (var msg in chat.Messages.OrderBy(m => m.SequenceNumber))
            {
                var role = msg.Role switch
                {
                    MessageRole.User => ChatRole.User,
                    MessageRole.Assistant => ChatRole.Assistant,
                    MessageRole.System => ChatRole.System,
                    MessageRole.Tool => ChatRole.Tool,
                    _ => ChatRole.User
                };
                messages.Add(new ChatMessage(role, msg.Content));
            }

            var chatClient = chatClientFactory.Create(command.Model);
            var response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);

            var promptTokens = (int)(response.Usage?.InputTokenCount ?? 0);
            var completionTokens = (int)(response.Usage?.OutputTokenCount ?? 0);
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
                Content = response.Text ?? string.Empty,
                Model = command.Model,
                FinishReason = response.FinishReason?.ToString(),
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
    }
}
