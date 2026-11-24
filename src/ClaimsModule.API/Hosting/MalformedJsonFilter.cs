using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ClaimsModule.API.Hosting;

/// <summary>
/// A JSON body that can't be parsed at all is a <b>400</b> (your P5 review decision): the request is not
/// a request. A body that parses but has wrong types or unknown enum names stays a 422 through model
/// binding, like every other validation error (D-72).
/// <para>
/// The two can't be told apart reliably after model binding: MVC keeps only the error message, and the
/// System.Text.Json messages differ only in wording. So the syntax is checked first, by parsing the
/// buffered body once (bodies here are small; multipart uploads are not JSON and are skipped). An empty
/// body is left to model binding ("a non-empty request body is required", 422).
/// </para>
/// </summary>
internal sealed class MalformedJsonFilter : IAsyncResourceFilter
{
    public const string Code = "REQUEST_MALFORMED";

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (request.HasJsonContentType() && request.ContentLength is not 0)
        {
            request.EnableBuffering();
            try
            {
                using var _ = await JsonDocument.ParseAsync(request.Body, cancellationToken: context.HttpContext.RequestAborted);
            }
            catch (JsonException ex)
            {
                if (request.Body.Length == 0)
                {
                    request.Body.Position = 0;
                    await next(); // an empty body: model binding answers 422 "body required"
                    return;
                }

                context.Result = new ApiErrorResult(new ApiError
                {
                    Type = "MalformedRequest",
                    Title = "The request body is not valid JSON.",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = $"JSON syntax error at line {(ex.LineNumber ?? 0) + 1}, position {(ex.BytePositionInLine ?? 0) + 1}.",
                    Codes = new Dictionary<string, string[]> { [ApiError.GeneralField] = [Code] },
                });
                return;
            }
            finally
            {
                request.Body.Position = 0;
            }
        }

        await next();
    }
}
