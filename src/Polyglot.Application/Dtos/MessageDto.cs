using Polyglot.Domain.Enums;

namespace Polyglot.Application.Dtos;

public record MessageDto
{
    public required Guid Id { get; init; }
    public required MessageRole Role { get; init; }
    public required string Content { get; init; }
    public string? Model { get; init; }
    public string? ToolCalls { get; init; }
    public string? ToolCallId { get; init; }
    public string? FinishReason { get; init; }
    public required int SequenceNumber { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required List<AttachmentDto> Attachments { get; init; }
}
