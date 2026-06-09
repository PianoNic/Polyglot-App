using Mediator;
using Microsoft.EntityFrameworkCore;
using Polyglot.Application.Dtos;
using Polyglot.Application.Models;
using Polyglot.Infrastructure;

namespace Polyglot.Application.Queries
{
    public record GetAdminSettingsQuery() : IQuery<Result<AdminSettingsDto>>;

    public class GetAdminSettingsQueryHandler(PolyglotDbContext dbContext) : IQueryHandler<GetAdminSettingsQuery, Result<AdminSettingsDto>>
    {
        public async ValueTask<Result<AdminSettingsDto>> Handle(GetAdminSettingsQuery query, CancellationToken cancellationToken)
        {
            var settings = await dbContext.AdminSettings.SingleAsync(cancellationToken);
            return Result<AdminSettingsDto>.Success(new AdminSettingsDto
            {
                MaxPricePerMillionTokens = settings.MaxPricePerMillionTokens,
                ActiveModelListMode = settings.ActiveModelListMode,
                StartingBalance = settings.StartingBalance,
                CostMultiplier = settings.CostMultiplier,
                CreditsPerUsd = settings.CreditsPerUsd,
            });
        }
    }
}
