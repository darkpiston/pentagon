# Pentagon

Azure Functions app for processing image submissions with Google Cloud Vision.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Azure Functions Core Tools v4](https://learn.microsoft.com/en-us/azure/azure-functions/functions-run-local)
- [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli) (for local Key Vault access)
- [Azurite](https://learn.microsoft.com/en-us/azure/storage/common/storage-use-azurite) (optional, for local storage emulation)

> **Deployment note:** .NET 10 is not supported on the **Linux Consumption** plan. Use **Flex Consumption**, **Premium**, or **Dedicated** when deploying to Linux.

## Getting started

1. Clone the repository.
2. Copy the local settings template:

   ```bash
   cp src/Pentagon.Functions/local.settings.json.example src/Pentagon.Functions/local.settings.json
   ```

3. Sign in to Azure and confirm your account can read the Key Vault secret:

   ```bash
   az login
   az keyvault secret show --vault-name tribes --name GoogleVisionCredentials --query "name" -o tsv
   ```

   Your user needs the **Key Vault Secrets User** role on vault `tribes`. In Azure, the Function App uses its managed identity for the same access.

4. Build and run from the project directory:

   ```bash
   cd src/Pentagon.Functions
   dotnet build
   func start
   ```

## Configuration

| Setting | Required | Description |
|---------|----------|-------------|
| `KeyVaultUri` | Yes | Azure Key Vault URI (e.g. `https://tribes.vault.azure.net/`) |
| `GoogleVisionSecretName` | No | Secret name for the Google service account JSON (default: `GoogleVisionCredentials`) |
| `GoogleVisionConfidenceThreshold` | No | Minimum confidence for motorcycle detection (default: `0.8`) |

These settings are configured as app settings on the `tribesfunction` Function App in Azure and mirrored in `local.settings.json` for local development.

## API contract

`POST /api/ProcessImage`

Request body (JSON):

```json
{
  "imageUrl": "https://example.com/image.jpg",
  "email": "user@example.com",
  "phone": "+15551234567"
}
```

| Field      | Required | Description              |
|------------|----------|--------------------------|
| `imageUrl` | Yes      | Public URL of the image to analyze |
| `email`    | No       | Contact email address    |
| `phone`    | No       | Contact phone number     |

The image URL must be publicly reachable by Google Cloud Vision.

Response body (JSON):

```json
{
  "result": "Pass",
  "faceDetected": true,
  "motorcycleDetected": true,
  "motorcycleConfidence": 0.91,
  "vision": {
    "faceCount": 1,
    "faces": [{ "detectionConfidence": 0.99, "boundingPoly": { "vertices": [{ "x": 0, "y": 0 }] } }],
    "motorcycle": {
      "source": "objectLocalization",
      "name": "Motorcycle",
      "score": 0.91,
      "boundingPoly": { "vertices": [{ "x": 10, "y": 20 }] }
    },
    "otherRelevantLabels": []
  }
}
```

| Field | Description |
|-------|-------------|
| `result` | `"Pass"` when a human face and motorcycle are both detected; otherwise `"Fail"` |
| `faceDetected` | Whether at least one face was detected |
| `motorcycleDetected` | Whether a motorcycle was detected above the confidence threshold |
| `motorcycleConfidence` | Best motorcycle detection score, if detected |
| `vision` | Trimmed Google Vision payload (faces, motorcycle, related labels) |

## Project structure

```
src/Pentagon.Functions/
├── Functions/ProcessImageFunction.cs   # HTTP trigger
├── Models/                             # Request/response DTOs
├── Services/                           # Vision + Key Vault integration
├── Program.cs                          # App entry point
├── host.json                           # Functions host config
└── local.settings.json.example         # Local dev settings template
```