using System.Net;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using Pentagon.Functions.Models;

namespace Pentagon.Functions.Services;

public interface IMessageService
{
    Task SendAsync(
        ProcessImageRequest request,
        ProcessImageResult result,
        string responseMessage,
        CancellationToken cancellationToken = default);
}

public sealed class MessageService : IMessageService
{
    private const string DefaultSecretName = "GoogleMail";
    private const string DefaultFromAddress = "hello@tribes.zone";
    private const string DefaultFromDisplayName = "Tribes";
    private const string SmtpHost = "smtp.gmail.com";
    private const int SmtpPort = 587;

    private readonly Lazy<Task<string>> _appPassword;
    private readonly string _fromAddress;
    private readonly string _fromDisplayName;
    private readonly ILogger<MessageService> _logger;

    public MessageService(IConfiguration configuration, ILogger<MessageService> logger)
    {
        _logger = logger;

        var keyVaultUri = configuration["KeyVaultUri"]
            ?? throw new InvalidOperationException("KeyVaultUri is not configured.");

        var secretName = configuration["GoogleMailSecretName"] ?? DefaultSecretName;
        _fromAddress = configuration["MailFromAddress"] ?? DefaultFromAddress;
        _fromDisplayName = configuration["MailFromDisplayName"] ?? DefaultFromDisplayName;

        _appPassword = new Lazy<Task<string>>(() => LoadAppPasswordAsync(keyVaultUri, secretName));
    }

    public async Task SendAsync(
        ProcessImageRequest request,
        ProcessImageResult result,
        string responseMessage,
        CancellationToken cancellationToken = default)
    {
        var appPassword = await _appPassword.Value;
        var subject = GetSubject(result);
        var htmlBody = VerificationEmailTemplate.Build(request, result, responseMessage);

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_fromDisplayName, _fromAddress));
        message.To.Add(MailboxAddress.Parse(request.Email));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlBody };

        _logger.LogInformation("Sending verification email to {Email}", request.Email);

        using var client = new SmtpClient();
        await client.ConnectAsync(SmtpHost, SmtpPort, SecureSocketOptions.StartTls, cancellationToken);
        await client.AuthenticateAsync(_fromAddress, appPassword, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);

        _logger.LogInformation("Verification email sent to {Email}", request.Email);
    }

    private static async Task<string> LoadAppPasswordAsync(string keyVaultUri, string secretName)
    {
        var secretClient = new SecretClient(new Uri(keyVaultUri), new DefaultAzureCredential());
        var secret = await secretClient.GetSecretAsync(secretName);
        var value = secret.Value.Value;

        var normalized = NormalizeAppPassword(value);
        if (normalized.Length == 0)
        {
            throw new InvalidOperationException($"Key Vault secret '{secretName}' is empty.");
        }

        return normalized;
    }

    private static string NormalizeAppPassword(string value) =>
        string.Concat(value.Where(c => !char.IsWhiteSpace(c)));

    private static string GetSubject(ProcessImageResult result) =>
        string.Equals(result.Result, "Pass", StringComparison.OrdinalIgnoreCase)
            ? "Profile Verified"
            : "Profile Verification Failed";
}

internal static class VerificationEmailTemplate
{
    private const string Header = "Tribes Profile Verification";
    private const string FailureHeader = "Your Profile Verification Failed";

    public static string Build(ProcessImageRequest request, ProcessImageResult result, string responseMessage)
    {
        var subtitle = GetSubtitle(result);
        var message = FormatMessage(result, responseMessage);
        var encodedMessage = WebUtility.HtmlEncode(message).Replace("\n", "<br>", StringComparison.Ordinal);
        var imageUrl = request.ImageUrl;
        var headerBackgroundColor = GetHeaderBackgroundColor(result);

        return $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1.0" />
              <title>{WebUtility.HtmlEncode(Header)}</title>
            </head>
            <body style="margin:0;padding:0;background-color:#f4f4f5;font-family:Arial,Helvetica,sans-serif;color:#18181b;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#f4f4f5;padding:24px 0;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="max-width:600px;width:100%;background-color:#ffffff;border-radius:8px;overflow:hidden;">
                      <tr>
                        <td style="padding:32px 32px 16px 32px;background-color:{headerBackgroundColor};color:#ffffff;">
                          <h1 style="margin:0;font-size:24px;line-height:1.3;font-weight:700;">{WebUtility.HtmlEncode(Header)}</h1>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:24px 32px 8px 32px;">
                          <h2 style="margin:0 0 16px 0;font-size:18px;line-height:1.4;font-weight:600;color:#18181b;">{WebUtility.HtmlEncode(subtitle)}</h2>
                          <p style="margin:0 0 24px 0;font-size:16px;line-height:1.6;color:#3f3f46;">{encodedMessage}</p>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:0 32px 32px 32px;">
                          <p style="margin:0 0 12px 0;font-size:14px;line-height:1.4;font-weight:600;color:#52525b;">Submitted photo</p>
                          <a href="{imageUrl}" style="display:inline-block;text-decoration:none;">
                            <img src="{imageUrl}" alt="Submitted photo" width="400" style="max-width:100%;height:auto;border-radius:4px;border:1px solid #e4e4e7;" />
                          </a>
                          <p style="margin:12px 0 0 0;font-size:14px;line-height:1.5;color:#52525b;word-break:break-all;">
                            <a href="{imageUrl}" style="color:#2563eb;text-decoration:underline;">{WebUtility.HtmlEncode(imageUrl)}</a>
                          </p>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }

    private static string GetHeaderBackgroundColor(ProcessImageResult result) =>
        string.Equals(result.Result, "Pass", StringComparison.OrdinalIgnoreCase)
            ? "#15803d"
            : "#b91c1c";

    private static string GetSubtitle(ProcessImageResult result) =>
        string.Equals(result.Result, "Pass", StringComparison.OrdinalIgnoreCase)
            ? "Your profile is verified"
            : "Your profile verification failed";

    private static string FormatMessage(ProcessImageResult result, string responseMessage)
    {
        if (string.Equals(result.Result, "Pass", StringComparison.OrdinalIgnoreCase))
        {
            return responseMessage;
        }

        var message = responseMessage.Trim();
        if (message.StartsWith(FailureHeader, StringComparison.OrdinalIgnoreCase))
        {
            var lines = message.Split('\n', 2, StringSplitOptions.TrimEntries);
            message = lines.Length > 1 ? lines[1] : string.Empty;
        }

        return message;
    }
}