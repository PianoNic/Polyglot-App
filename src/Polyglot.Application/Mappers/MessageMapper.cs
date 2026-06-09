using Polyglot.Application.Dtos;
using Polyglot.Domain;

namespace Polyglot.Application.Mappers
{
    public static class MessageMapper
    {
        public static MessageDto ToDto(this Message message)
        {
            return new MessageDto
            {
                Id = message.Id,
                Role = message.Role,
                Content = message.Content,
                Model = message.Model,
                ToolCalls = message.ToolCalls,
                ToolCallId = message.ToolCallId,
                FinishReason = message.FinishReason,
                SequenceNumber = message.SequenceNumber,
                CreatedAt = message.CreatedAt,
            };
        }
    }
}
