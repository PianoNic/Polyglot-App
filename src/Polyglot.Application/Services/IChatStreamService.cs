using Polyglot.Application.Command;
using Polyglot.Application.Dtos;

namespace Polyglot.Application.Services
{
    public interface IChatStreamService
    {
        IAsyncEnumerable<ChatStreamEvent> StreamMessageAsync(SendMessageCommand command, CancellationToken cancellationToken);
    }

    public abstract record ChatStreamEvent;
    public sealed record ChatStreamChunk(string Text) : ChatStreamEvent;
    public sealed record ChatStreamDone(SendMessageDto Result) : ChatStreamEvent;
    public sealed record ChatStreamError(string Message) : ChatStreamEvent;
}
