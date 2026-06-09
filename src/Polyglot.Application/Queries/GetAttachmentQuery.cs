using Mediator;
using Microsoft.EntityFrameworkCore;
using Polyglot.Application.Models;
using Polyglot.Infrastructure;
using Polyglot.Infrastructure.Services;

namespace Polyglot.Application.Queries
{
    public record GetAttachmentQuery(Guid AttachmentId) : IQuery<Result<AttachmentContent>>;

    public record AttachmentContent(string FileName, string MediaType, byte[] Data);

    public class GetAttachmentQueryHandler(IUserService userService, PolyglotDbContext dbContext) : IQueryHandler<GetAttachmentQuery, Result<AttachmentContent>>
    {
        public async ValueTask<Result<AttachmentContent>> Handle(GetAttachmentQuery query, CancellationToken cancellationToken)
        {
            var userId = await userService.GetCurrentUserIdAsync(cancellationToken);
            var attachment = await dbContext.Attachments
                .SingleOrDefaultAsync(a => a.Id == query.AttachmentId && a.UserId == userId, cancellationToken);

            if (attachment is null)
                return Result<AttachmentContent>.Failure("Attachment not found");

            return Result<AttachmentContent>.Success(new AttachmentContent(attachment.FileName, attachment.MediaType, attachment.Data));
        }
    }
}
