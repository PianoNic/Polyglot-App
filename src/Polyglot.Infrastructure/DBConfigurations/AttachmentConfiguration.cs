using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Polyglot.Domain;

namespace Polyglot.Infrastructure.DBConfigurations
{
    public class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
    {
        public void Configure(EntityTypeBuilder<Attachment> builder)
        {
            builder.HasKey(a => a.Id);

            builder.HasIndex(a => a.MessageId);
            builder.HasIndex(a => a.UserId);
            builder.Property(a => a.FileName).HasMaxLength(255);
            builder.Property(a => a.MediaType).HasMaxLength(100);

            builder.HasOne<Message>()
                .WithMany()
                .HasForeignKey(a => a.MessageId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
