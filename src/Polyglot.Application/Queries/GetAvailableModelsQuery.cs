using Mediator;
using Microsoft.EntityFrameworkCore;
using Polyglot.Application.Models;
using Polyglot.Domain.Enums;
using Polyglot.Infrastructure;
using Polyglot.Infrastructure.Dtos;

namespace Polyglot.Application.Queries
{
    public record GetAvailableModelsQuery() : IQuery<Result<List<AvailableModelDto>>>;

    public class GetAvailableModelsQueryHandler(PolyglotDbContext dbContext) : IQueryHandler<GetAvailableModelsQuery, Result<List<AvailableModelDto>>>
    {
        public async ValueTask<Result<List<AvailableModelDto>>> Handle(GetAvailableModelsQuery query, CancellationToken cancellationToken)
        {
            var settings = await dbContext.AdminSettings.SingleAsync(cancellationToken);

            var modelsQuery = dbContext.Models.AsQueryable();

            if (settings.ActiveModelListMode == ModelListMode.Whitelist)
            {
                var whitelistIds = dbContext.ModelListEntries
                    .Where(e => e.ListType == ModelListType.Whitelist)
                    .Select(e => e.ModelId);
                modelsQuery = modelsQuery.Where(m => whitelistIds.Contains(m.ModelId));
            }
            else if (settings.ActiveModelListMode == ModelListMode.Blacklist)
            {
                var blacklistIds = dbContext.ModelListEntries
                    .Where(e => e.ListType == ModelListType.Blacklist)
                    .Select(e => e.ModelId);
                modelsQuery = modelsQuery.Where(m => !blacklistIds.Contains(m.ModelId));
            }

            if (settings.MaxPricePerMillionTokens is not null)
            {
                var cap = settings.MaxPricePerMillionTokens.Value;
                modelsQuery = modelsQuery.Where(m => m.PromptPricePerMillion <= cap && m.CompletionPricePerMillion <= cap);
            }

            var models = await modelsQuery
                .Select(m => new AvailableModelDto
                {
                    Id = m.ModelId,
                    Name = m.Name,
                    Provider = m.ModelId.Contains("/") ? m.ModelId.Substring(0, m.ModelId.IndexOf("/")) : string.Empty,
                    Currency = "USD",
                    ContextLength = m.ContextLength,
                    InputModalities = m.InputModalities,
                    OutputModalities = m.OutputModalities,
                    InputPricePer1M = m.PromptPricePerMillion,
                    OutputPricePer1M = m.CompletionPricePerMillion,
                })
                .ToListAsync(cancellationToken);

            return Result<List<AvailableModelDto>>.Success(models);
        }
    }
}
