using Azure;
using Google;
using MailKit.Net.Smtp;
using MailKit.Security;
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
    private readonly IMessageService _messageService;
    private readonly ILogger<VerifyImageFunction> _logger;

    public VerifyImageFunction(
        IImageAnalyzerService imageAnalyzer,
        IInterpreterService interpreter,
        IMessageService messageService,
        ILogger<VerifyImageFunction> logger)
    {
        _imageAnalyzer = imageAnalyzer;
        _interpreter = interpreter;
        _messageService = messageService;
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
            var cancellationToken = req.HttpContext.RequestAborted;
            var result = await _imageAnalyzer.AnalyzeAsync(request!, cancellationToken);
            var message = await _interpreter.InterpretAsync(result, cancellationToken);
            await _messageService.SendAsync(request!, result, message, cancellationToken);
            return new OkObjectResult(new { message, emailSent = true });
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
        catch (Exception ex) when (ex is SmtpCommandException or SmtpProtocolException or AuthenticationException or IOException)
        {
            _logger.LogError(ex, "Failed to send verification email.");
            return new ObjectResult(new { error = "Unable to send verification email. Please try again later." })
            {
                StatusCode = StatusCodes.Status502BadGateway,
            };
        }
    }
}
