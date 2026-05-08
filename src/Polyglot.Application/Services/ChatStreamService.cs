using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Polyglot.Application.Command;
using Polyglot.Application.Dtos;
using Polyglot.Application.Mappers;
using Polyglot.Domain;
using Polyglot.Domain.Enums;
using Polyglot.Infrastructure;
using Polyglot.Infrastructure.Extensions;
using Polyglot.Infrastructure.Services;

namespace Polyglot.Application.Services
{
    public class ChatStreamService(IUserService userService, PolyglotDbContext dbContext, IChatClientFactory chatClientFactory, ICreditsService creditsService) : IChatStreamService
    {
        public async IAsyncEnumerable<ChatStreamEvent> StreamMessageAsync(SendMessageCommand command, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var preflight = await PreflightAsync(command, cancellationToken);
            if (preflight is { Error: { } error })
            {
                yield return new ChatStreamError(error);
                yield break;
            }

            var ctx = preflight.Context!;
            var chatClient = chatClientFactory.Create(command.Model!);

            var assistantContent = new StringBuilder();
            UsageDetails? usage = null;
            ChatFinishReason? finishReason = null;

            await foreach (var update in chatClient.GetStreamingResponseAsync(ctx.Messages, cancellationToken: cancellationToken))
            {
                if (update.FinishReason is { } fr) finishReason = fr;

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

            var done = await FinalizeAsync(ctx, assistantContent.ToString(), finishReason, usage, cancellationToken);
            yield return new ChatStreamDone(done);
        }

        private async Task<PreflightResult> PreflightAsync(SendMessageCommand command, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(command.Model))
                return new PreflightResult { Error = "Model is required" };

            var userId = await userService.GetCurrentUserIdAsync(cancellationToken);
            var user = await dbContext.Users.SingleAsync(u => u.Id == userId, cancellationToken);

            if (user.IsLocked)
                return new PreflightResult { Error = "Your account has been locked. Please contact an administrator." };

            var model = await dbContext.Models.SingleOrDefaultAsync(m => m.ModelId == command.Model, cancellationToken);
            if (model is null)
                return new PreflightResult { Error = $"Model '{command.Model}' not found" };

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
                Context = new PreflightContext(chat, model, user, userMessage, messages, command.Model!, nextSequence)
            };
        }

        private async Task<SendMessageDto> FinalizeAsync(PreflightContext ctx, string assistantText, ChatFinishReason? finishReason, UsageDetails? usage, CancellationToken cancellationToken)
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
                Model = ctx.ModelId,
                FinishReason = finishReason?.ToString(),
                TokenUsage = $"{promptTokens}/{completionTokens}",
                SequenceNumber = ctx.UserSequence + 1
            };
            ctx.Chat.Messages.Add(assistantMessage);

            if (ctx.Chat.Title == "New Chat" && ctx.UserSequence == 0)
            {
                ctx.Chat.Title = ctx.UserMessage.Content.Length > 50
                    ? ctx.UserMessage.Content[..50] + "..."
                    : ctx.UserMessage.Content;
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            return new SendMessageDto(ctx.Chat.Id, ctx.UserMessage.ToDto(), assistantMessage.ToDto());
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
            string ModelId,
            int UserSequence);
    }
}
