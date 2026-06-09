namespace Polyglot.Application.Dtos;

public record SendMessageDto
{
    public required Guid ChatId { get; init; }
    public required string ChatTitle { get; init; }
    public required MessageDto UserMessage { get; init; }
    public required MessageDto AssistantMessage { get; init; }
}
