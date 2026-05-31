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

    private const string FailureHeader = "Your Profile Verification Failed";

    private const string SystemInstructionText = """
        You write the explanation shown to a user after motorcycle rider profile photo verification failed.
        Output ONLY the explanation body. Do not include a title or header.
        Write exactly 2 complete sentences.
        You may describe what the photo shows using up to 2 image content hints joined with "or" (for example, "a plant or flower"). Never list more than 2 items.
        State why verification failed, then encourage the user to upload a clear photo showing both their face and their motorcycle.
        Every sentence must end with a period. Never end with a comma or leave a sentence unfinished.
        Do not mention scores, confidence, APIs, bounding boxes, or any internal detection methods.
        Example body: "It looks like you uploaded a photo of a flower or plant. Please upload a clear photo that shows both your face and your motorcycle."
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
                MaxOutputTokens = 1024,
                ThinkingConfig = new ThinkingConfig
                {
                    ThinkingBudget = 0,
                },
            },
            cancellationToken: cancellationToken);

        var message = ExtractText(response);
        if (!string.IsNullOrWhiteSpace(message) && IsCompleteFailureBody(message, response))
        {
            return FormatFailureMessage(message);
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            _logger.LogWarning(
                "Gemini returned an incomplete failure message: {Message}. FinishReason: {FinishReason}",
                message,
                response.Candidates?.FirstOrDefault()?.FinishReason);
        }
        else
        {
            _logger.LogWarning("Gemini returned empty content; using fallback failure message.");
        }

        return BuildFallbackMessage(result);
    }

    private static string BuildUserPrompt(ProcessImageResult result)
    {
        var labels = result.Vision.Labels
            .Select(l => l.Description)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Take(2)
            .Select(d => d.ToLowerInvariant())
            .ToList();

        var labelText = labels.Count switch
        {
            0 => "none",
            1 => labels[0],
            _ => $"{labels[0]} or {labels[1]}",
        };

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

    private static bool IsCompleteFailureBody(string body, GenerateContentResponse response)
    {
        body = body.Trim().Trim('"');
        if (body.Length < 40)
        {
            return false;
        }

        var finishReason = response.Candidates?.FirstOrDefault()?.FinishReason;
        if (finishReason == FinishReason.MaxTokens)
        {
            return false;
        }

        if (!body.Contains("motorcycle", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var words = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return false;
        }

        var lastWord = words[^1].TrimEnd('.', ',', ';', ':');
        if (IncompleteEndingWords.Contains(lastWord))
        {
            return false;
        }

        return body.Contains('.', StringComparison.Ordinal);
    }

    private static readonly HashSet<string> IncompleteEndingWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "or", "and", "a", "an", "the", "of", "with", "your", "shows", "showing", "like", "that", "this", "photo",
    };

    private static string FormatFailureMessage(string body)
    {
        body = body.Trim().Trim('"');
        if (body.StartsWith(FailureHeader, StringComparison.OrdinalIgnoreCase))
        {
            var lines = body.Split('\n', 2, StringSplitOptions.TrimEntries);
            body = lines.Length > 1 ? lines[1] : string.Empty;
        }

        body = EnsureEndsWithPeriod(body);
        return $"{FailureHeader}\n{body}";
    }

    private static string EnsureEndsWithPeriod(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        text = text.TrimEnd();
        while (text.EndsWith(',') || text.EndsWith(';'))
        {
            text = text[..^1].TrimEnd();
        }

        if (!text.EndsWith('.'))
        {
            text += '.';
        }

        return text;
    }

    private static string BuildFallbackMessage(ProcessImageResult result)
    {
        var labels = result.Vision.Labels
            .Select(l => l.Description)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Take(2)
            .Select(d => d.ToLowerInvariant())
            .ToList();

        string body;
        if (!result.FaceDetected && !result.MotorcycleDetected)
        {
            body = labels.Count switch
            {
                0 => "We couldn't find your face or a motorcycle in this photo. Please upload a clear photo that shows both your face and your motorcycle.",
                1 => $"It looks like your photo shows a {labels[0]}. Please upload a clear photo that shows both your face and your motorcycle.",
                _ => $"It looks like your photo shows a {labels[0]} or {labels[1]}. Please upload a clear photo that shows both your face and your motorcycle.",
            };
        }
        else if (!result.FaceDetected)
        {
            body = "We couldn't find your face in this photo. Please upload a clear photo that shows both your face and your motorcycle.";
        }
        else
        {
            body = "We couldn't find a motorcycle in this photo. Please upload a clear photo that shows both your face and your motorcycle.";
        }

        return FormatFailureMessage(body);
    }
}
