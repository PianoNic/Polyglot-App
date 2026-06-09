using Polyglot.Domain.Enums;

namespace Polyglot.Application.Dtos;

public record ModelListEntryDto
{
    public required Guid Id { get; init; }
    public required string ModelId { get; init; }
    public required ModelListType ListType { get; init; }
}
