using ClaimsModule.Application.Abstractions;
using Serilog.Context;

namespace ClaimsModule.API.Hosting;

/// <summary>
/// Correlation id per request (S14.2, task 33): taken from the <c>X-Correlation-Id</c> header when it is
/// a GUID, otherwise new. It is echoed on the response, added to every log event of the request, and
/// copied onto every audit entry the request writes (through <see cref="ICorrelationContext"/>).
/// </summary>
internal sealed class CorrelationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ICorrelationContext correlation)
    {
        var id = correlation.CorrelationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[RequestCorrelationContext.HeaderName] = id?.ToString();
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", id))
        {
            await next(context);
        }
    }
}
