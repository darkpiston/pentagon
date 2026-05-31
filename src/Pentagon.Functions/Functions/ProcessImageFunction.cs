using System.ComponentModel.DataAnnotations;
using Azure;
using Google;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Pentagon.Functions.Models;
using Pentagon.Functions.Services;

namespace Pentagon.Functions.Functions;

public class ProcessImageFunction
{
    private readonly IImageAnalyzerService _imageAnalyzer;
    private readonly ILogger<ProcessImageFunction> _logger;

    public ProcessImageFunction(
        IImageAnalyzerService imageAnalyzer,
        ILogger<ProcessImageFunction> logger)
    {
        _imageAnalyzer = imageAnalyzer;
        _logger = logger;
    }

    [Function("ProcessImage")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        ProcessImageRequest? request;
        try
        {
            request = await req.ReadFromJsonAsync<ProcessImageRequest>(req.HttpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to deserialize request body.");
            return new BadRequestObjectResult(new { error = "Invalid JSON request body." });
        }

        if (request is null)
        {
            return new BadRequestObjectResult(new { error = "Request body is required." });
        }

        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(request);
        if (!Validator.TryValidateObject(request, validationContext, validationResults, validateAllProperties: true))
        {
            return new BadRequestObjectResult(new
            {
                error = "Invalid request.",
                details = validationResults.Select(r => r.ErrorMessage).Where(m => m is not null),
            });
        }

        try
        {
            var result = await _imageAnalyzer.AnalyzeAsync(request, req.HttpContext.RequestAborted);
            return new OkObjectResult(result);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to retrieve Google Vision credentials from Key Vault.");
            return new ObjectResult(new { error = "Unable to retrieve credentials. Please try again later." })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable,
            };
        }
        catch (GoogleApiException ex)
        {
            _logger.LogError(ex, "Google Vision API request failed.");
            return new ObjectResult(new { error = "Image analysis failed. Please try again later." })
            {
                StatusCode = StatusCodes.Status502BadGateway,
            };
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Google Vision API error:", StringComparison.Ordinal))
        {
            _logger.LogError(ex, "Google Vision API returned an error for the image.");
            return new ObjectResult(new { error = "Image analysis failed. Please verify the image URL is publicly accessible." })
            {
                StatusCode = StatusCodes.Status502BadGateway,
            };
        }
    }
}
