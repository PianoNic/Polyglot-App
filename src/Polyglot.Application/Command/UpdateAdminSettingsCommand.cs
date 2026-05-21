using Mediator;
using Microsoft.EntityFrameworkCore;
using Polyglot.Application.Dtos;
using Polyglot.Application.Models;
using Polyglot.Domain.Enums;
using Polyglot.Infrastructure;

namespace Polyglot.Application.Command
{
    public record UpdateAdminSettingsCommand(
        decimal? MaxPricePerMillionTokens,
        ModelListMode ActiveModelListMode,
        long StartingBalance,
        decimal CostMultiplier,
        decimal CreditsPerUsd) : ICommand<Result<AdminSettingsDto>>;

    public class UpdateAdminSettingsCommandHandler(PolyglotDbContext dbContext) : ICommandHandler<UpdateAdminSettingsCommand, Result<AdminSettingsDto>>
    {
        public async ValueTask<Result<AdminSettingsDto>> Handle(UpdateAdminSettingsCommand command, CancellationToken cancellationToken)
        {
            if (command.CreditsPerUsd <= 0)
                return Result<AdminSettingsDto>.Failure("CreditsPerUsd must be greater than zero");
            if (command.CostMultiplier <= 0)
                return Result<AdminSettingsDto>.Failure("CostMultiplier must be greater than zero");
            if (command.StartingBalance < 0)
                return Result<AdminSettingsDto>.Failure("StartingBalance cannot be negative");

            var settings = await dbContext.AdminSettings.SingleAsync(cancellationToken);
            settings.MaxPricePerMillionTokens = command.MaxPricePerMillionTokens;
            settings.ActiveModelListMode = command.ActiveModelListMode;
            settings.StartingBalance = command.StartingBalance;
            settings.CostMultiplier = command.CostMultiplier;
            settings.CreditsPerUsd = command.CreditsPerUsd;

            await dbContext.SaveChangesAsync(cancellationToken);

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
