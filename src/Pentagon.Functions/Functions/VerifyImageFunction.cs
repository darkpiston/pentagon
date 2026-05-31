using Azure;
using Google;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Pentagon.Functions.Services;

namespace Pentagon.Functions.Functions;

public class VerifyImageFunction
{
    private readonly IImageAnalyzerService _imageAnalyzer;
    private readonly IInterpreterService _interpreter;
    private readonly ILogger<VerifyImageFunction> _logger;

    public VerifyImageFunction(
        IImageAnalyzerService imageAnalyzer,
        IInterpreterService interpreter,
        ILogger<VerifyImageFunction> logger)
    {
        _imageAnalyzer = imageAnalyzer;
        _interpreter = interpreter;
        _logger = logger;
    }

    [Function("VerifyImage")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        var (request, error) = await ProcessImageRequestExtensions.TryBindAsync(req, _logger);
        if (error is not null)
        {
            return error;
        }

        try
        {
            var result = await _imageAnalyzer.AnalyzeAsync(request!, req.HttpContext.RequestAborted);
            var message = await _interpreter.InterpretAsync(result, req.HttpContext.RequestAborted);
            return new OkObjectResult(new { message });
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to retrieve Google credentials from Key Vault.");
            return new ObjectResult(new { error = "Unable to retrieve credentials. Please try again later." })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable,
            };
        }
        catch (GoogleApiException ex)
        {
            _logger.LogError(ex, "Google API request failed.");
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
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to generate verification message with Gemini.");
            return new ObjectResult(new { error = "Unable to generate verification message. Please try again later." })
            {
                StatusCode = StatusCodes.Status502BadGateway,
            };
        }
    }
}
