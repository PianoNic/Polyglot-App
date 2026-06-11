namespace Polyglot.Infrastructure.Dtos;

public record AvailableModelDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Provider { get; init; }
    public required string Currency { get; init; }
    public required int ContextLength { get; init; }
    public required List<string> InputModalities { get; init; }
    public required List<string> OutputModalities { get; init; }
    public required List<string> SupportedParameters { get; init; }
    public required decimal InputPricePer1M { get; init; }
    public required decimal OutputPricePer1M { get; init; }
}
