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
            return Result<AppDto>.Success(new AppDto
            {
                Authority = configuration["Oidc:Authority"] ?? string.Empty,
                ClientId = configuration["Oidc:ClientId"] ?? string.Empty,
                RedirectUri = configuration["Oidc:RedirectUri"] ?? "http://localhost:4200/",
                PostLogoutRedirectUri = configuration["Oidc:PostLogoutRedirectUri"] ?? "http://localhost:4200/",
                Scope = configuration["Oidc:Scope"] ?? "openid profile email roles",
                Version = "1.0.0",
            });
        }
    }
}
