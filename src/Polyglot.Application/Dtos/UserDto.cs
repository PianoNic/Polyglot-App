using Polyglot.Domain.Enums;

namespace Polyglot.Application.Dtos;

public record UserDto
{
    public required Guid Id { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public string? AvatarUrl { get; init; }
    public required UserRole Role { get; init; }
    public required long CreditBalance { get; init; }
    public required bool IsLocked { get; init; }
}
