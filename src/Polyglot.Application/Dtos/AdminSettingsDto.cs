using Polyglot.Domain.Enums;

namespace Polyglot.Application.Dtos;

public record AdminSettingsDto
{
    public decimal? MaxPricePerMillionTokens { get; init; }
    public required ModelListMode ActiveModelListMode { get; init; }
    public required long StartingBalance { get; init; }
    public required decimal CostMultiplier { get; init; }
    public required decimal CreditsPerUsd { get; init; }
}
