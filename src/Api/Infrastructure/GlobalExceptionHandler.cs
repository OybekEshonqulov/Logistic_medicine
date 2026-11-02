using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaTrack.Domain.Common;

namespace PharmaTrack.Api.Infrastructure;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (code, args) = exception switch
        {
            DomainException domain => (domain.Code, domain.Args),
            DbUpdateException => (ErrorCodes.Conflict, []),
            ValidationFailure failure => (ErrorCodes.ValidationFailed, [failure.Errors]),
            _ => (ErrorCodes.InternalError, Array.Empty<object>())
        };

        if (code == ErrorCodes.InternalError) logger.LogError(exception, "Unhandled exception");

        var problem = Problems.Create(context, code);
        if (exception is ValidationFailure validation) problem.Extensions["errors"] = validation.Errors;
        if (exception is DomainException { Args.Length: > 0 }) problem.Extensions["details"] = args;

        context.Response.StatusCode = problem.Status ?? 500;
        await context.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", ct);
        return true;
    }
}

public sealed class ValidationFailure(IDictionary<string, string[]> errors) : Exception
{
    public IDictionary<string, string[]> Errors { get; } = errors;
}

public static class Problems
{
    public static ProblemDetails Create(HttpContext context, string code)
    {
        var problem = new ProblemDetails
        {
            Status = ErrorCatalog.StatusOf(code),
            Title = ErrorCatalog.MessageOf(code),
            Instance = context.Request.Path
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = context.TraceIdentifier;
        return problem;
    }
}
