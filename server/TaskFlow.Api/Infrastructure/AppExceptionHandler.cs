using Microsoft.AspNetCore.Diagnostics;

namespace TaskFlow.Api.Infrastructure;

/// <summary>
/// Converts our AppExceptions into JSON responses like { "message": "...", "current": {...} }.
/// Any other exception falls through to the default handler (a generic 500, no stack trace).
/// </summary>
public class AppExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        if (exception is not AppException appException)
            return false;

        http.Response.StatusCode = appException.StatusCode;
        object body = appException is ConflictException conflict
            ? new { message = conflict.Message, current = conflict.Current }
            : new { message = appException.Message };

        await http.Response.WriteAsJsonAsync(body, ct);
        return true;
    }
}
