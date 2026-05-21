namespace Polyglot.Application.Dtos;

public record ChatDetailDto
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
    public required List<MessageDto> Messages { get; init; }
}
