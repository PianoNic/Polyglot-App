using System.Runtime.CompilerServices;
using System.Text;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Polyglot.Application.Dtos;
using Polyglot.Application.Mappers;
using Polyglot.Domain;
using Polyglot.Domain.Enums;
using Polyglot.Infrastructure;
using Polyglot.Infrastructure.Extensions;
using Polyglot.Infrastructure.Services;

namespace Polyglot.Application.Command
{
    public record SendMessageCommand(Guid? ChatId, string Message, string Model) : IStreamCommand<ChatStreamEvent>;

    public abstract record ChatStreamEvent;
    public sealed record ChatStreamChunk(string Text) : ChatStreamEvent;
    public sealed record ChatStreamDone(SendMessageDto Result) : ChatStreamEvent;
    public sealed record ChatStreamError(string Message) : ChatStreamEvent;

    public class SendMessageCommandHandler(IUserService userService, PolyglotDbContext dbContext, IChatClientFactory chatClientFactory, ICreditsService creditsService, IChatTitleGenerator titleGenerator) : IStreamCommandHandler<SendMessageCommand, ChatStreamEvent>
    {
        public async IAsyncEnumerable<ChatStreamEvent> Handle(SendMessageCommand command, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var preflight = await PreflightAsync(command, cancellationToken);
            if (preflight.Error is not null)
            {
                yield return new ChatStreamError(preflight.Error);
                yield break;
            }

            var ctx = preflight.Context!;
            var chatClient = chatClientFactory.Create(command.Model);

            var assistantContent = new StringBuilder();
            UsageDetails? usage = null;
            ChatFinishReason? finishReason = null;

            await foreach (var update in chatClient.GetStreamingResponseAsync(ctx.Messages, cancellationToken: cancellationToken))
            {
                if (update.FinishReason is { } fr)
                    finishReason = fr;

                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case TextContent { Text: { Length: > 0 } text }:
                            assistantContent.Append(text);
                            yield return new ChatStreamChunk(text);
                            break;
                        case UsageContent uc:
                            usage = uc.Details;
                            break;
                    }
                }
            }

            var done = await FinalizeAsync(command, ctx, assistantContent.ToString(), finishReason, usage, cancellationToken);
            yield return new ChatStreamDone(done);
        }

        private async Task<PreflightResult> PreflightAsync(SendMessageCommand command, CancellationToken cancellationToken)
        {
            var userId = await userService.GetCurrentUserIdAsync(cancellationToken);
            var user = await dbContext.Users.SingleAsync(u => u.Id == userId, cancellationToken);

            if (user.IsLocked)
                return new PreflightResult { Error = "Your account has been locked. Please contact an administrator." };

            var model = await dbContext.Models.SingleOrDefaultAsync(m => m.ModelId == command.Model, cancellationToken);
            if (model is null)
                return new PreflightResult { Error = $"Model '{command.Model}' not found" };

            var settings = await dbContext.AdminSettings.SingleAsync(cancellationToken);

            if (settings.ActiveModelListMode == ModelListMode.Whitelist)
            {
                var isWhitelisted = await dbContext.ModelListEntries
                    .AnyAsync(e => e.ListType == ModelListType.Whitelist && e.ModelId == command.Model, cancellationToken);
                if (!isWhitelisted)
                    return new PreflightResult { Error = $"Model '{command.Model}' is not available" };
            }
            else if (settings.ActiveModelListMode == ModelListMode.Blacklist)
            {
                var isBlacklisted = await dbContext.ModelListEntries
                    .AnyAsync(e => e.ListType == ModelListType.Blacklist && e.ModelId == command.Model, cancellationToken);
                if (isBlacklisted)
                    return new PreflightResult { Error = $"Model '{command.Model}' is not available" };
            }

            if (settings.MaxPricePerMillionTokens is not null
                && (model.PromptPricePerMillion > settings.MaxPricePerMillionTokens
                    || model.CompletionPricePerMillion > settings.MaxPricePerMillionTokens))
                return new PreflightResult { Error = $"Model '{command.Model}' is not available" };

            Chat chat;
            if (command.ChatId is not null)
            {
                var existing = await dbContext.Chats
                    .Include(c => c.Messages.OrderBy(m => m.SequenceNumber))
                    .SingleOrDefaultAsync(c => c.Id == command.ChatId.Value && c.UserId == userId, cancellationToken);
                if (existing is null)
                    return new PreflightResult { Error = "Chat not found" };
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
                return new PreflightResult { Error = $"Insufficient credits (need ~{worstCaseCredits}, have {user.CreditBalance})" };

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

            return new PreflightResult
            {
                Context = new PreflightContext(chat, model, user, userMessage, messages, nextSequence)
            };
        }

        private async Task<SendMessageDto> FinalizeAsync(SendMessageCommand command, PreflightContext ctx, string assistantText, ChatFinishReason? finishReason, UsageDetails? usage, CancellationToken cancellationToken)
        {
            var promptTokens = (int)(usage?.InputTokenCount ?? 0);
            var completionTokens = (int)(usage?.OutputTokenCount ?? 0);
            var actualCredits = await creditsService.CalculateChatCreditsAsync(
                promptTokens,
                completionTokens,
                ctx.Model.PromptPricePerMillion,
                ctx.Model.CompletionPricePerMillion,
                cancellationToken);

            ctx.User.CreditBalance -= actualCredits;

            var assistantMessage = new Message
            {
                ChatId = ctx.Chat.Id,
                Role = MessageRole.Assistant,
                Content = assistantText,
                Model = command.Model,
                FinishReason = finishReason?.ToString(),
                TokenUsage = $"{promptTokens}/{completionTokens}",
                SequenceNumber = ctx.UserSequence + 1
            };
            ctx.Chat.Messages.Add(assistantMessage);
            dbContext.Messages.Add(assistantMessage);

            var isFirstExchange = ctx.Chat.Title == "New Chat" && ctx.UserSequence == 0;
            if (isFirstExchange)
            {
                ctx.Chat.Title = command.Message.Length > 50
                    ? command.Message[..50] + "..."
                    : command.Message;
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            if (isFirstExchange)
            {
                var placeholderTitle = ctx.Chat.Title;
                var chatId = ctx.Chat.Id;
                var userText = command.Message;
                _ = Task.Run(() => titleGenerator.GenerateAndSaveAsync(chatId, userText, assistantText, placeholderTitle));
            }

            return new SendMessageDto
            {
                ChatId = ctx.Chat.Id,
                ChatTitle = ctx.Chat.Title,
                UserMessage = ctx.UserMessage.ToDto(),
                AssistantMessage = assistantMessage.ToDto(),
            };
        }

        private sealed class PreflightResult
        {
            public string? Error { get; init; }
            public PreflightContext? Context { get; init; }
        }

        private sealed record PreflightContext(
            Chat Chat,
            Domain.Model Model,
            User User,
            Message UserMessage,
            List<ChatMessage> Messages,
            int UserSequence);
    }
}
