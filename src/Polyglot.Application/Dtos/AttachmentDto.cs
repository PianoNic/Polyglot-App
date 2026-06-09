namespace Polyglot.Application.Dtos;

public record AttachmentDto
{
    public required Guid Id { get; init; }
    public required string FileName { get; init; }
    public required string MediaType { get; init; }
    public required long SizeBytes { get; init; }
}
