using Polyglot.Domain.Enums;

namespace Polyglot.Domain
{
    public class AdminSettings : BaseEntity
    {
        public static readonly Guid SingletonId = new("00000000-0000-0000-0000-000000000001");

        public AdminSettings()
        {
            Id = SingletonId;
        }

        public decimal? MaxPricePerMillionTokens { get; set; }
        public ModelListMode ActiveModelListMode { get; set; } = ModelListMode.None;
        public long StartingBalance { get; set; } = 10_000;
        public decimal CostMultiplier { get; set; } = 1.0m;
        public decimal CreditsPerUsd { get; set; } = 10_000m;
    }
}
