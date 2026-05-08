using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Polyglot.API.Extensions;
using Polyglot.Infrastructure;
using Polyglot.Infrastructure.Clients;
using Polyglot.Infrastructure.Extensions;
using Polyglot.Infrastructure.Services;
using Polyglot.Infrastructure.BackgroundServices;
using Polyglot.Application.Services;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// Controllers + JSON
builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Swagger / OpenAPI
builder.Services.AddSwaggerGen(options =>
{
    var authority = builder.Configuration["Oidc:Authority"]
        ?? throw new InvalidOperationException("Oidc:Authority not configured");

    options.AddSecurityDefinition("OpenIdConnect", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.OpenIdConnect,
        OpenIdConnectUrl = new Uri($"{authority}/.well-known/openid-configuration"),
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("OpenIdConnect", document)] = new List<string>()
    });
});

// Mediator & Validation
builder.Services.AddMediator(options => { options.ServiceLifetime = ServiceLifetime.Scoped; });

// DBContext
builder.Services.AddDbContext<PolyglotDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("PolyglotDatabase"),
        npgsqlOptions => npgsqlOptions
            .EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorCodesToAdd: null
            )
    ));

builder.Services.AddHttpClient();
builder.Services.AddAgentFramework(builder.Configuration);
builder.Services.AddHttpContextAccessor();

// CORS
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? throw new InvalidOperationException("Cors:AllowedOrigins not configured");

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()));

// Services
builder.Services.AddScoped<IOidcService, OidcService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IOpenRouterClient, OpenRouterClient>();
builder.Services.AddScoped<ICreditsService, CreditsService>();
builder.Services.AddScoped<IChatStreamService, ChatStreamService>();
builder.Services.AddHostedServices();

// Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Oidc:Authority"];
        options.RequireHttpsMetadata = builder.Configuration.GetValue("Oidc:RequireHttpsMetadata", true);
        options.TokenValidationParameters.NameClaimType = "name";
        options.TokenValidationParameters.RoleClaimType = "roles";
        options.TokenValidationParameters.ValidateAudience = false;
    })
    .AddUserSync();

// Authorization
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

var app = builder.Build();

// DB
app.ApplyMigrations();
await app.ApplySeedsAsync();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.OAuthClientId(builder.Configuration["Oidc:ClientId"]);
        options.OAuthUsePkce();
    });
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();