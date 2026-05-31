using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Vision.V1;
using Microsoft.Extensions.Configuration;

namespace Pentagon.Functions.Services;

public sealed class GoogleVisionCredentialProvider
{
    private const string DefaultSecretName = "GoogleVisionCredentials";

    private readonly Lazy<Task<ImageAnnotatorClient>> _client;

    public GoogleVisionCredentialProvider(IConfiguration configuration)
    {
        var keyVaultUri = configuration["KeyVaultUri"]
            ?? throw new InvalidOperationException("KeyVaultUri is not configured.");

        var secretName = configuration["GoogleVisionSecretName"] ?? DefaultSecretName;

        _client = new Lazy<Task<ImageAnnotatorClient>>(() => CreateClientAsync(keyVaultUri, secretName));
    }

    public Task<ImageAnnotatorClient> GetClientAsync() => _client.Value;

    private static async Task<ImageAnnotatorClient> CreateClientAsync(string keyVaultUri, string secretName)
    {
        var secretClient = new SecretClient(new Uri(keyVaultUri), new DefaultAzureCredential());
        var secret = await secretClient.GetSecretAsync(secretName);

        var credential = GoogleCredential.FromJson(secret.Value.Value);
        return await new ImageAnnotatorClientBuilder
        {
            Credential = credential,
        }.BuildAsync();
    }
}
