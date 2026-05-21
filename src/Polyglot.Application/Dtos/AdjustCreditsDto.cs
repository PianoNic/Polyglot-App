using Polyglot.Domain.Enums;

namespace Polyglot.Application.Dtos;

public record AdjustCreditsDto
{
    public required long Amount { get; init; }
    public required CreditAdjustmentMode Mode { get; init; }
}
