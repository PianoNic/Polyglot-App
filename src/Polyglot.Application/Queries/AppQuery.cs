using Mediator;
using Microsoft.Extensions.Configuration;
using Polyglot.Application.Dtos;
using Polyglot.Application.Models;

namespace Polyglot.Application.Queries
{
    public record AppQuery() : IQuery<Result<AppDto>>;

    public class AppQueryHandler(IConfiguration configuration) : IQueryHandler<AppQuery, Result<AppDto>>
    {
        public async ValueTask<Result<AppDto>> Handle(AppQuery query, CancellationToken cancellationToken)
        {
            return Result<AppDto>.Success(new AppDto(
                configuration["Oidc:Authority"] ?? string.Empty,
                configuration["Oidc:ClientId"] ?? string.Empty,
                configuration["Oidc:RedirectUri"] ?? "http://localhost:4200/",
                configuration["Oidc:PostLogoutRedirectUri"] ?? "http://localhost:4200/",
                configuration["Oidc:Scope"] ?? "openid profile email groups picture offline_access",
                "1.0.0"
            ));
        }
    }
}
