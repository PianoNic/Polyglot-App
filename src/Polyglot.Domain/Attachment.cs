namespace Polyglot.Domain
{
    public class Attachment : BaseEntity
    {
        public required Guid UserId { get; init; }
        public Guid? MessageId { get; set; }
        public required string FileName { get; init; }
        public required string MediaType { get; init; }
        public required byte[] Data { get; init; }
        public required long SizeBytes { get; init; }
    }
}
