using Mediator;
using Microsoft.EntityFrameworkCore;
using Polyglot.Application.Dtos;
using Polyglot.Application.Mappers;
using Polyglot.Application.Models;
using Polyglot.Infrastructure;
using Polyglot.Infrastructure.Services;

namespace Polyglot.Application.Queries
{
    public record GetChatQuery(Guid ChatId) : IQuery<Result<ChatDetailDto>>;

    public class GetChatQueryHandler(IUserService userService, PolyglotDbContext dbContext) : IQueryHandler<GetChatQuery, Result<ChatDetailDto>>
    {
        public async ValueTask<Result<ChatDetailDto>> Handle(GetChatQuery query, CancellationToken cancellationToken)
        {
            var userId = await userService.GetCurrentUserIdAsync(cancellationToken);
            var chat = await dbContext.Chats
                .Include(c => c.Messages.OrderBy(m => m.SequenceNumber))
                .SingleOrDefaultAsync(c => c.Id == query.ChatId && c.UserId == userId, cancellationToken);

            if (chat is null)
                return Result<ChatDetailDto>.Failure("Chat not found");

            var messageIds = chat.Messages.Select(m => m.Id).ToList();
            var attachments = await dbContext.Attachments
                .Where(a => a.MessageId != null && messageIds.Contains(a.MessageId.Value))
                .Select(a => new
                {
                    MessageId = a.MessageId!.Value,
                    Dto = new AttachmentDto
                    {
                        Id = a.Id,
                        FileName = a.FileName,
                        MediaType = a.MediaType,
                        SizeBytes = a.SizeBytes,
                    },
                })
                .ToListAsync(cancellationToken);
            var attachmentsByMessage = attachments.ToLookup(a => a.MessageId, a => a.Dto);

            return Result<ChatDetailDto>.Success(chat.ToDetailDto(attachmentsByMessage));
        }
    }
}
