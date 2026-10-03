namespace TaskFlow.Api.Infrastructure;

/// <summary>
/// Services throw these and AppExceptionHandler turns them into HTTP status codes.
/// This keeps services free of HTTP details (and easy to unit test).
/// </summary>
public abstract class AppException : Exception
{
    protected AppException(string message) : base(message) { }
    public abstract int StatusCode { get; }
}

public class NotFoundException : AppException
{
    public NotFoundException(string message) : base(message) { }
    public override int StatusCode => StatusCodes.Status404NotFound;
}

public class ForbiddenException : AppException
{
    public ForbiddenException(string message) : base(message) { }
    public override int StatusCode => StatusCodes.Status403Forbidden;
}

public class BadRequestException : AppException
{
    public BadRequestException(string message) : base(message) { }
    public override int StatusCode => StatusCodes.Status400BadRequest;
}

/// <summary>
/// Thrown when someone else saved the same task first. <see cref="Current"/> carries
/// the latest version so the client can show it to the user.
/// </summary>
public class ConflictException : AppException
{
    public ConflictException(string message, object? current = null) : base(message) => Current = current;
    public object? Current { get; }
    public override int StatusCode => StatusCodes.Status409Conflict;
}
