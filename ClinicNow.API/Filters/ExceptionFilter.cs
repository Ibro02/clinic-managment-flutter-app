using System.Net;
using ClinicNow.Model.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ClinicNow.API.Filters;

/// <summary>
/// Maps the <see cref="ClinicNowException"/> hierarchy to HTTP status codes with
/// clean, user-safe messages. Anything that is <em>not</em> a
/// <see cref="ClinicNowException"/> is treated as an unexpected bug: logged in full
/// via <see cref="ILogger{TCategoryName}"/> with enough context to reproduce it
/// (rulebook Part II §B), and returned to the client as a generic 500 - no stack
/// trace or internal detail, except in Development where the detail is additionally
/// included to speed up local debugging (rulebook §5: stack traces are forbidden
/// specifically "za non-development okruzenja").
/// </summary>
public class ExceptionFilter : ExceptionFilterAttribute
{
    private const string GenericServerErrorMessage = "Došlo je do greške na serveru. Pokušajte ponovo kasnije.";

    private readonly ILogger<ExceptionFilter> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionFilter(ILogger<ExceptionFilter> logger, IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public override void OnException(ExceptionContext context)
    {
        var (statusCode, errors) = context.Exception switch
        {
            ValidationException validation => (HttpStatusCode.BadRequest, validation.Errors),
            BusinessException business => (HttpStatusCode.BadRequest, SingleError(business.Message)),
            NotFoundException notFound => (HttpStatusCode.NotFound, SingleError(notFound.Message)),
            ForbiddenException forbidden => (HttpStatusCode.Forbidden, SingleError(forbidden.Message)),
            AuthenticationException authentication => (HttpStatusCode.Unauthorized, SingleError(authentication.Message)),
            _ => (HttpStatusCode.InternalServerError, SingleError(GenericServerErrorMessage))
        };

        if (context.Exception is ClinicNowException)
        {
            _logger.LogWarning(context.Exception, "Handled {ExceptionType} on {Path}: {Message}",
                context.Exception.GetType().Name, context.HttpContext.Request.Path, context.Exception.Message);
        }
        else
        {
            _logger.LogError(context.Exception, "Unhandled exception on {Method} {Path}",
                context.HttpContext.Request.Method, context.HttpContext.Request.Path);
        }

        context.HttpContext.Response.StatusCode = (int)statusCode;

        object body = _environment.IsDevelopment() && statusCode == HttpStatusCode.InternalServerError
            ? new { errors, detail = context.Exception.ToString() }
            : new { errors };

        context.Result = new JsonResult(body);
        context.ExceptionHandled = true;
    }

    private static IDictionary<string, string[]> SingleError(string message) =>
        new Dictionary<string, string[]> { ["error"] = [message] };
}
