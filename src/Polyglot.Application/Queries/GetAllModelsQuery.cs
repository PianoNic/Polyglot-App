using Mediator;
using Microsoft.EntityFrameworkCore;
using Polyglot.Application.Models;
using Polyglot.Infrastructure;
using Polyglot.Infrastructure.Dtos;

namespace Polyglot.Application.Queries
{
    public record GetAllModelsQuery() : IQuery<Result<List<AvailableModelDto>>>;

    public class GetAllModelsQueryHandler(PolyglotDbContext dbContext) : IQueryHandler<GetAllModelsQuery, Result<List<AvailableModelDto>>>
    {
        public async ValueTask<Result<List<AvailableModelDto>>> Handle(GetAllModelsQuery query, CancellationToken cancellationToken)
        {
            var models = await dbContext.Models
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
