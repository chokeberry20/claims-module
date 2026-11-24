using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ClaimsModule.API.Hosting;

/// <summary>Swagger with Bearer authentication and the Idempotency-Key header (B4.3, B6.2 "Swagger populated with correct schemas").</summary>
/// <summary>
/// Readable Swagger schema ids: the type's own name (<c>CreateClaimRequest</c>, <c>ClaimStatus</c>), and for
/// generics the name plus its arguments (<c>PagedResultOfClaimSummaryDto</c>) instead of .NET's
/// <c>PagedResult`1[[…, Version=…]]</c>. Swashbuckle refuses to generate the document if two types map to the
/// same id, so a clash can't go unnoticed: the Swagger tests generate it.
/// </summary>
internal static class SchemaIds
{
    public static string For(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
        return name + "Of" + string.Concat(type.GetGenericArguments().Select(For));
    }
}

internal static class SwaggerSetup
{
    public const string BearerScheme = "Bearer";

    public static IServiceCollection AddClaimsSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Claims Module API",
                Version = "v1",
                Description = "FNOL intake and reserve management (DICEUS technical assessment). "
                    + "Sign in with POST /api/auth/token (demo users), then use Authorize with the token. "
                    + "Errors use the S10.4 body (type, title, status, errors); writes accept an Idempotency-Key header.",
            });
            options.AddSecurityDefinition(BearerScheme, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "The accessToken from POST /api/auth/token.",
            });
            options.OperationFilter<AuthAndIdempotencyOperationFilter>();
            options.SupportNonNullableReferenceTypes();
            options.CustomSchemaIds(SchemaIds.For);

            var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
            if (File.Exists(xml))
            {
                options.IncludeXmlComments(xml);
            }
        });
        return services;
    }
}

/// <summary>Marks secured operations with the Bearer requirement and adds the Idempotency-Key header to writes.</summary>
internal sealed class AuthAndIdempotencyOperationFilter : IOperationFilter
{
    public const string IdempotencyHeader = "Idempotency-Key";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        var anonymous = metadata.OfType<IAllowAnonymous>().Any();
        if (!anonymous)
        {
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = SwaggerSetup.BearerScheme } }] = [],
            });
        }

        if (metadata.OfType<IdempotentAttribute>().Any())
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = IdempotencyHeader,
                In = ParameterLocation.Header,
                Required = false,
                Description = "Optional (S10). Repeating a request with the same key and body returns the first result "
                    + "instead of running it again (24 h); the same key with a different body gives 409. 1-200 characters.",
                Schema = new OpenApiSchema { Type = "string", MaxLength = Controllers.ApiControllerBase.MaxIdempotencyKeyLength, Example = new OpenApiString("7f1c0b9e-2d4a-4c1e-9b8f-3a6d5e2c1f00") },
            });
        }
    }
}

/// <summary>The action is a write that honours the Idempotency-Key header (S10, D-66).</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class IdempotentAttribute : Attribute;
