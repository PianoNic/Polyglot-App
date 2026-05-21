namespace Polyglot.Application.Dtos;

public record SetUserLockDto
{
    public required bool IsLocked { get; init; }
}
