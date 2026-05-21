using Microsoft.EntityFrameworkCore;
using Polyglot.Domain;
using Polyglot.Domain.Enums;


namespace Polyglot.Infrastructure.Services
{
    public class UserService(IOidcService oidcService, PolyglotDbContext dbContext) : IUserService
    {
        public async Task<bool> ExistsAsync(string externalId, CancellationToken cancellationToken = default)
        {
            return await dbContext.Users.AnyAsync(u => u.ExternalId == externalId, cancellationToken);
        }

        public async Task<Guid> GetCurrentUserIdAsync(CancellationToken cancellationToken = default)
        {
            var oidcUser = await oidcService.GetCurrentUserAsync(cancellationToken)
                ?? throw new UnauthorizedAccessException("No authenticated user");

            var user = await dbContext.Users
                .SingleOrDefaultAsync(u => u.ExternalId == oidcUser.ExternalId, cancellationToken)
                ?? throw new UnauthorizedAccessException("User not found");

            return user.Id;
        }

        public async Task SyncCurrentUserAsync(CancellationToken cancellationToken = default)
        {
            var oidcUser = await oidcService.GetCurrentUserAsync(cancellationToken)
                ?? throw new UnauthorizedAccessException("No authenticated user");

            var user = await dbContext.Users
                .SingleOrDefaultAsync(u => u.ExternalId == oidcUser.ExternalId, cancellationToken);

            var role = oidcUser.Roles.Contains("admin") ? UserRole.Admin : UserRole.User;
            var email = oidcUser.Email ?? string.Empty;
            var displayName = oidcUser.DisplayName ?? "Polyglot User";

            if (user is null)
            {
                var settings = await dbContext.AdminSettings.SingleAsync(cancellationToken);

                user = new User
                {
                    ExternalId = oidcUser.ExternalId,
                    Email = email,
                    DisplayName = displayName,
                    AvatarUrl = oidcUser.AvatarUrl,
                    Role = role,
                    CreditBalance = settings.StartingBalance,
                    Preferences = new UserPreferences()
                };

                dbContext.Users.Add(user);

                try
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    dbContext.Entry(user).State = EntityState.Detached;
                    if (!await dbContext.Users.AnyAsync(u => u.ExternalId == oidcUser.ExternalId, cancellationToken))
                        throw;
                }
                return;
            }

            user.Email = email;
            user.DisplayName = displayName;
            user.AvatarUrl = oidcUser.AvatarUrl;
            user.Role = role;

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
