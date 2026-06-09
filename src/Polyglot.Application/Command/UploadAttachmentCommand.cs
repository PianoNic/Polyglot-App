using Mediator;
using Polyglot.Application.Dtos;
using Polyglot.Application.Models;
using Polyglot.Domain;
using Polyglot.Infrastructure;
using Polyglot.Infrastructure.Services;

namespace Polyglot.Application.Command
{
    public record UploadAttachmentCommand(string FileName, string MediaType, byte[] Data) : ICommand<Result<AttachmentDto>>;

    public class UploadAttachmentCommandHandler(IUserService userService, PolyglotDbContext dbContext) : ICommandHandler<UploadAttachmentCommand, Result<AttachmentDto>>
    {
        public const long MaxSizeBytes = 5 * 1024 * 1024;

        public static readonly HashSet<string> AllowedMediaTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "image/png",
            "image/jpeg",
            "image/webp",
            "image/gif",
            "application/pdf",
            "text/plain",
            "text/markdown",
            "text/csv",
        };

        public async ValueTask<Result<AttachmentDto>> Handle(UploadAttachmentCommand command, CancellationToken cancellationToken)
        {
            if (command.Data.Length == 0)
                return Result<AttachmentDto>.Failure("File is empty");

            if (command.Data.Length > MaxSizeBytes)
                return Result<AttachmentDto>.Failure($"File exceeds the {MaxSizeBytes / (1024 * 1024)} MB limit");

            if (!AllowedMediaTypes.Contains(command.MediaType))
                return Result<AttachmentDto>.Failure($"File type '{command.MediaType}' is not supported");

            var userId = await userService.GetCurrentUserIdAsync(cancellationToken);

            var attachment = new Attachment
            {
                UserId = userId,
                FileName = command.FileName,
                MediaType = command.MediaType,
                Data = command.Data,
                SizeBytes = command.Data.Length,
            };
            dbContext.Attachments.Add(attachment);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Result<AttachmentDto>.Success(new AttachmentDto
            {
                Id = attachment.Id,
                FileName = attachment.FileName,
                MediaType = attachment.MediaType,
                SizeBytes = attachment.SizeBytes,
            });
        }
    }
}
