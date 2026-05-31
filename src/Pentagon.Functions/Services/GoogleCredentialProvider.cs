using System.Text.Json;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Vision.V1;
using Google.GenAI;
using Microsoft.Extensions.Configuration;

namespace Pentagon.Functions.Services;

public sealed class GoogleCredentialProvider
{
    private const string DefaultSecretName = "GoogleVisionCredentials";
    private const string DefaultLocation = "us-central1";
    private static readonly string[] GenAiScopes =
    [
        "https://www.googleapis.com/auth/cloud-platform",
    ];

    private readonly Lazy<Task<GoogleCredentialBundle>> _bundle;
    private readonly Lazy<Task<ImageAnnotatorClient>> _visionClient;
    private readonly Lazy<Task<Client>> _genAiClient;
    private readonly string _location;

    public GoogleCredentialProvider(IConfiguration configuration)
    {
        var keyVaultUri = configuration["KeyVaultUri"]
            ?? throw new InvalidOperationException("KeyVaultUri is not configured.");

        var secretName = configuration["GoogleVisionSecretName"] ?? DefaultSecretName;
        _location = configuration["GoogleCloudLocation"] ?? DefaultLocation;

        _bundle = new Lazy<Task<GoogleCredentialBundle>>(() => LoadBundleAsync(keyVaultUri, secretName));
        _visionClient = new Lazy<Task<ImageAnnotatorClient>>(CreateVisionClientAsync);
        _genAiClient = new Lazy<Task<Client>>(CreateGenAiClientAsync);
    }

    public async Task<GoogleCredential> GetCredentialAsync()
    {
        var bundle = await _bundle.Value;
        return bundle.Credential;
    }

    public Task<ImageAnnotatorClient> GetVisionClientAsync() => _visionClient.Value;

    public Task<Client> GetGenAiClientAsync() => _genAiClient.Value;

    private async Task<GoogleCredentialBundle> LoadBundleAsync(string keyVaultUri, string secretName)
    {
        var secretClient = new SecretClient(new Uri(keyVaultUri), new DefaultAzureCredential());
        var secret = await secretClient.GetSecretAsync(secretName);
        var json = secret.Value.Value;

        var credential = GoogleCredential.FromJson(json);
        var projectId = ParseProjectId(json);

        return new GoogleCredentialBundle(credential, projectId);
    }

    private async Task<ImageAnnotatorClient> CreateVisionClientAsync()
    {
        var bundle = await _bundle.Value;
        return await new ImageAnnotatorClientBuilder
        {
            Credential = bundle.Credential,
        }.BuildAsync();
    }

    private async Task<Client> CreateGenAiClientAsync()
    {
        var bundle = await _bundle.Value;
        var credential = bundle.Credential.IsCreateScopedRequired
            ? bundle.Credential.CreateScoped(GenAiScopes)
            : bundle.Credential;

        return new Client(
            vertexAI: true,
            credential: credential,
            project: bundle.ProjectId,
            location: _location);
    }

    private static string ParseProjectId(string serviceAccountJson)
    {
        using var document = JsonDocument.Parse(serviceAccountJson);
        if (document.RootElement.TryGetProperty("project_id", out var projectIdElement)
            && projectIdElement.GetString() is { Length: > 0 } projectId)
        {
            return projectId;
        }

        throw new InvalidOperationException("Google service account JSON is missing project_id.");
    }

    private sealed record GoogleCredentialBundle(GoogleCredential Credential, string ProjectId);
}
