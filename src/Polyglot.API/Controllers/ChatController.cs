// Streaming via Server-Sent Events uses .NET 10's built-in `TypedResults.ServerSentEvents`.
// Refs:
//   - https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0 (Server-Sent Events)
//   - https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.ichatclient.getstreamingresponseasync
//   - https://www.milanjovanovic.tech/blog/server-sent-events-in-aspnetcore-and-dotnet-10
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Polyglot.Application.Command;
using Polyglot.Application.Dtos;
using Polyglot.Application.Queries;

namespace Polyglot.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChatController(IMediator mediator) : ControllerBase
    {
        [HttpGet]
        [ProducesResponseType(typeof(List<ChatDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetChats(CancellationToken cancellationToken)
        {
            var result = await mediator.Send(new GetChatsQuery(), cancellationToken);
            if (result.IsSuccess)
                return Ok(result.Value);

            return BadRequest(result.Error);
        }

        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(ChatDetailDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> GetChat(Guid id, CancellationToken cancellationToken)
        {
            var result = await mediator.Send(new GetChatQuery(id), cancellationToken);
            if (result.IsSuccess)
                return Ok(result.Value);

            return BadRequest(result.Error);
        }

        [HttpPost]
        [Produces("text/event-stream")]
        [ProducesResponseType(typeof(ChatStreamPayload), StatusCodes.Status200OK)]
        public ServerSentEventsResult<ChatStreamPayload> SendMessage([FromBody] SendMessageCommand command, CancellationToken cancellationToken)
            => TypedResults.ServerSentEvents(StreamMessage(command, cancellationToken));

        private async IAsyncEnumerable<SseItem<ChatStreamPayload>> StreamMessage(
            SendMessageCommand command,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var evt in mediator.CreateStream(command, cancellationToken))
            {
                yield return evt switch
                {
                    ChatStreamChunk c => new SseItem<ChatStreamPayload>(new ChatStreamPayload(ChatStreamPayloadType.Chunk, Text: c.Text), "chunk"),
                    ChatStreamDone d => new SseItem<ChatStreamPayload>(new ChatStreamPayload(ChatStreamPayloadType.Done, Result: d.Result), "done"),
                    ChatStreamError e => new SseItem<ChatStreamPayload>(new ChatStreamPayload(ChatStreamPayloadType.Error, Error: e.Message), "error"),
                    _ => throw new InvalidOperationException($"Unknown stream event: {evt.GetType().Name}"),
                };
            }
        }

        [HttpPut("{id:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> RenameChat(Guid id, [FromBody] RenameChatCommand command, CancellationToken cancellationToken)
        {
            var result = await mediator.Send(command with { ChatId = id }, cancellationToken);
            if (result.IsSuccess)
                return NoContent();

            return BadRequest(result.Error);
        }

        [HttpDelete("{id:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> DeleteChat(Guid id, CancellationToken cancellationToken)
        {
            var result = await mediator.Send(new DeleteChatCommand(id), cancellationToken);
            if (result.IsSuccess)
                return NoContent();

            return BadRequest(result.Error);
        }
    }

    /// <summary>
    /// One frame of the chat response stream. The discriminator lives in the
    /// payload itself (not only in the SSE event name) so the same schema works
    /// over any transport, e.g. WebSockets.
    /// </summary>
    public sealed record ChatStreamPayload(ChatStreamPayloadType Type, string? Text = null, SendMessageDto? Result = null, string? Error = null);

    public enum ChatStreamPayloadType
    {
        Chunk,
        Done,
        Error,
    }
}
