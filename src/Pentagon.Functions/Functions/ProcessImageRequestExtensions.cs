using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Pentagon.Functions.Models;

namespace Pentagon.Functions.Functions;

public static class ProcessImageRequestExtensions
{
    public static async Task<(ProcessImageRequest? Request, IActionResult? Error)> TryBindAsync(
        HttpRequest req,
        ILogger logger)
    {
        ProcessImageRequest? request;
        try
        {
            request = await req.ReadFromJsonAsync<ProcessImageRequest>(req.HttpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to deserialize request body.");
            return (null, new BadRequestObjectResult(new { error = "Invalid JSON request body." }));
        }

        if (request is null)
        {
            return (null, new BadRequestObjectResult(new { error = "Request body is required." }));
        }

        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(request);
        if (!Validator.TryValidateObject(request, validationContext, validationResults, validateAllProperties: true))
        {
            return (null, new BadRequestObjectResult(new
            {
                error = "Invalid request.",
                details = validationResults.Select(r => r.ErrorMessage).Where(m => m is not null),
            }));
        }

        return (request, null);
    }
}
