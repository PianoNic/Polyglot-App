namespace Polyglot.Domain
{
    public class Model : BaseEntity
    {
        public required string ModelId { get; init; }
        public required string Name { get; set; }
        public int ContextLength { get; set; }
        public List<string> InputModalities { get; set; } = [];
        public List<string> OutputModalities { get; set; } = [];
        public List<string> SupportedParameters { get; set; } = [];
        public decimal PromptPricePerMillion { get; set; }
        public decimal CompletionPricePerMillion { get; set; }
    }
}
