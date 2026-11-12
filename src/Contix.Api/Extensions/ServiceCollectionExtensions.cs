using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Contix.Api.Infrastructure;
using Contix.Application.Common.Abstractions;
using Contix.Application.Features.Documentos;
using Contix.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

namespace Contix.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        services.AddControllers(o => o.Filters.Add<ValidationFilter>())
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        // Erros de binding do modelo também retornam ProblemDetails (comportamento padrão do [ApiController]).
        services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
            ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier);
        services.AddExceptionHandler<GlobalExceptionHandler>();

        // Limite de upload = tamanho máximo por arquivo × quantidade máxima por envio.
        var docs = configuration.GetSection(DocumentoOptions.Secao).Get<DocumentoOptions>() ?? new DocumentoOptions();
        var limiteUpload = (long)docs.TamanhoMaximoMb * 1024 * 1024 * docs.MaximoArquivosPorUpload;
        services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = limiteUpload);
        services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(o => o.Limits.MaxRequestBodySize = limiteUpload);

        services.AddCors(o => o.AddDefaultPolicy(p => p
            .WithOrigins(configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders("Content-Disposition", CorrelationIdMiddleware.Header)));

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
        });

        return services.AddJwtAuthentication(configuration).AddSwagger();
    }

    private static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Configuração tardia: usa as JwtOptions já resolvidas (permite sobrescrever por ambiente/testes).
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((o, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "name",
                    RoleClaimType = ClaimTypes.Role
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Escritorio, p => p.RequireRole(Policies.RolesEscritorio))
            .AddPolicy(Policies.Administrador, p => p.RequireRole(nameof(Domain.Enums.PerfilUsuario.Administrador)));

        return services;
    }

    private static IServiceCollection AddSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(o =>
        {
            o.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Contix API",
                Version = "v1",
                Description = "API da plataforma Contix: gestão de escritórios contábeis, clientes e documentos."
            });

            o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Informe o access token obtido em /api/v1/auth/login."
            });
            o.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            });

            var xml = Path.Combine(AppContext.BaseDirectory, "Contix.Api.xml");
            if (File.Exists(xml)) o.IncludeXmlComments(xml);
            var xmlApp = Path.Combine(AppContext.BaseDirectory, "Contix.Application.xml");
            if (File.Exists(xmlApp)) o.IncludeXmlComments(xmlApp);
        });
        return services;
    }
}
