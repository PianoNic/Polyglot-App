namespace Polyglot.Application.Command
{
    public record SendMessageCommand(Guid? ChatId, string Message, string? Model);
}
