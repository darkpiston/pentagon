using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pentagon.Functions.Models;

namespace Pentagon.Functions.Services;

public interface IInterpreterService
{
    Task<string> InterpretAsync(ProcessImageResult result, CancellationToken cancellationToken = default);
}

public sealed class InterpreterService : IInterpreterService
{
    private const string PassMessage = "Your Profile is now verified. Thank you";
    private const string DefaultGeminiModel = "gemini-2.5-flash";

    private const string SystemInstructionText = """
        You explain why a motorcycle rider profile photo verification failed.
        Write exactly 2 sentences in plain, friendly language.
        Explain why verification failed and ask the user to upload a clear photo showing both their face and their motorcycle.
        Do not mention scores, confidence, APIs, bounding boxes, or any internal detection methods.
        Example: "It looks like you uploaded a photo of a flower. For verification, please upload a clear photo that shows both your face and your motorcycle."
        """;

    private readonly GoogleCredentialProvider _credentialProvider;
    private readonly string _model;
    private readonly ILogger<InterpreterService> _logger;

    public InterpreterService(
        GoogleCredentialProvider credentialProvider,
        IConfiguration configuration,
        ILogger<InterpreterService> logger)
    {
        _credentialProvider = credentialProvider;
        _logger = logger;
        _model = configuration["GoogleGeminiModel"] ?? DefaultGeminiModel;
    }

    public async Task<string> InterpretAsync(
        ProcessImageResult result,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(result.Result, "Pass", StringComparison.OrdinalIgnoreCase))
        {
            return PassMessage;
        }

        var userPrompt = BuildUserPrompt(result);
        var client = await _credentialProvider.GetGenAiClientAsync();

        _logger.LogInformation("Generating verification failure message with Gemini model {Model}", _model);

        var response = await client.Models.GenerateContentAsync(
            model: _model,
            contents: userPrompt,
            config: new GenerateContentConfig
            {
                SystemInstruction = new Content
                {
                    Parts = [new Part { Text = SystemInstructionText }],
                },
                Temperature = 0.3f,
                MaxOutputTokens = 256,
            },
            cancellationToken: cancellationToken);

        var message = ExtractText(response);
        if (!string.IsNullOrWhiteSpace(message))
        {
            return message.Trim();
        }

        _logger.LogWarning("Gemini returned empty content; using fallback failure message.");
        return BuildFallbackMessage(result);
    }

    private static string BuildUserPrompt(ProcessImageResult result)
    {
        var labels = result.Vision.Labels
            .Select(l => l.Description)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .ToList();

        var labelText = labels.Count > 0
            ? string.Join(", ", labels)
            : "none";

        return $"""
            Verification result: Fail
            Face detected: {result.FaceDetected.ToString().ToLowerInvariant()}
            Motorcycle detected: {result.MotorcycleDetected.ToString().ToLowerInvariant()}
            Image content hints: {labelText}
            """;
    }

    private static string? ExtractText(GenerateContentResponse response)
    {
        var candidate = response.Candidates?.FirstOrDefault();
        var parts = candidate?.Content?.Parts;
        if (parts is null || parts.Count == 0)
        {
            return null;
        }

        return string.Concat(parts.Select(p => p.Text).Where(t => !string.IsNullOrEmpty(t)));
    }

    private static string BuildFallbackMessage(ProcessImageResult result)
    {
        if (!result.FaceDetected && !result.MotorcycleDetected)
        {
            return "We couldn't find your face or a motorcycle in this photo. Please upload a clear photo that shows both your face and your motorcycle.";
        }

        if (!result.FaceDetected)
        {
            return "We couldn't find your face in this photo. Please upload a clear photo that shows both your face and your motorcycle.";
        }

        return "We couldn't find a motorcycle in this photo. Please upload a clear photo that shows both your face and your motorcycle.";
    }
}
