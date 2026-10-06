using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Application.Receipts;

namespace PurchaseMemory.Api.Errors;

public sealed partial class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            ReceiptCaptureNotFoundException or PurchaseDraftNotFoundException =>
                (StatusCodes.Status404NotFound, "Not found", exception.Message),
            PurchaseConcurrencyException =>
                (StatusCodes.Status409Conflict, "Purchase changed", exception.Message),
            PossibleDuplicatePurchaseException =>
                (StatusCodes.Status409Conflict, "Possible duplicate", exception.Message),
            ReceiptProcessingStateException or SavedReceiptInconsistencyException =>
                (StatusCodes.Status409Conflict, "Receipt state conflict", exception.Message),
            // Keeps the framework's own status (400, 413, 415…) for binding failures.
            BadHttpRequestException badRequest =>
                (badRequest.StatusCode, "Invalid request", "The request could not be read: malformed JSON or a value of the wrong type."),
            System.Text.Json.JsonException =>
                (StatusCodes.Status400BadRequest, "Invalid request", "The request body is not valid JSON of the expected shape."),
            InvalidDataException =>
                (StatusCodes.Status400BadRequest, "Invalid image", exception.Message),
            ArgumentException =>
                (StatusCodes.Status400BadRequest, "Invalid request", exception.Message),
            _ =>
                (StatusCodes.Status500InternalServerError, "Unexpected error", "The request could not be completed.")
        };

        if (status >= 500)
        {
            UnhandledRequest(exception);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail,
                Instance = httpContext.Request.Path
            },
            Exception = exception
        });
    }

    [LoggerMessage(
        EventId = 3101,
        Level = LogLevel.Error,
        Message = "Unhandled API request failure")]
    private partial void UnhandledRequest(Exception exception);
}
