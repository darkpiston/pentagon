# Pentagon

Azure Functions app for processing image submissions with Google Cloud Vision and Gemini.

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

3. Sign in to Azure and confirm your account can read the Key Vault secrets:

   ```bash
   az login
   az keyvault secret show --vault-name tribes --name GoogleVisionCredentials --query "name" -o tsv
   az keyvault secret show --vault-name tribes --name GoogleMail --query "name" -o tsv
   ```

   Your user needs the **Key Vault Secrets User** role on vault `tribes`. In Azure, the Function App uses its managed identity for the same access.

   The Google service account JSON (`GoogleVisionCredentials`) is used for Vision and Gemini Enterprise. Ensure it has **Vertex AI User** (or equivalent Gemini Enterprise) permissions on the GCP project.

   The Gmail app password (`GoogleMail`) is used to send verification emails from `hello@tribes.zone`.

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
| `GoogleCloudLocation` | No | GCP region for Gemini Enterprise (default: `us-central1`) |
| `GoogleGeminiModel` | No | Gemini model for user-facing failure messages (default: `gemini-2.5-flash`) |
| `GoogleMailSecretName` | No | Key Vault secret name for the Gmail app password (default: `GoogleMail`) |
| `MailFromAddress` | No | SMTP username and From address (default: `hello@tribes.zone`) |
| `MailFromDisplayName` | No | Display name for outgoing emails (default: `Tribes`) |

These settings are configured as app settings on the `tribesfunction` Function App in Azure and mirrored in `local.settings.json` for local development.

## API contract

`POST /api/ProcessImage`

Request body (JSON):

```json
{
  "imageUrl": "https://example.com/image.jpg",
  "email": "user@example.com"
}
```

| Field      | Required | Description              |
|------------|----------|--------------------------|
| `imageUrl` | Yes      | Public URL of the image to analyze |
| `email`    | Yes      | Contact email address    |

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
    "faces": [{ "detectionConfidence": 0.99, "visible": true, "boundingPoly": { "vertices": [{ "x": 0, "y": 0 }] } }],
    "motorcycle": {
      "source": "objectLocalization",
      "name": "Motorcycle",
      "score": 0.91,
      "boundingPoly": { "vertices": [{ "x": 10, "y": 20 }] }
    },
    "labels": [
      { "description": "Motorcycle", "score": 0.91 },
      { "description": "Vehicle", "score": 0.87 },
      { "description": "Person", "score": 0.85 }
    ],
    "otherRelevantLabels": []
  }
}
```

| Field | Description |
|-------|-------------|
| `result` | `"Pass"` when a clearly visible human face and motorcycle are both detected; otherwise `"Fail"` |
| `faceDetected` | Whether at least one clearly visible, unobstructed face was detected |
| `motorcycleDetected` | Whether a motorcycle was detected above the confidence threshold |
| `motorcycleConfidence` | Best motorcycle detection score, if detected |
| `vision` | Trimmed Google Vision payload (faces, motorcycle, content labels, related vehicle labels) |

### VerifyImage

`POST /api/VerifyImage`

Uses the same request body as `ProcessImage`. Runs image analysis, generates a plain-language verification message, and sends it as an HTML email to the address in `email`.

Response body (JSON):

```json
{
  "message": "Your Profile is now verified. Thank you",
  "emailSent": true
}
```

On failure, the message explains why verification failed based on what was detected (missing face, missing motorcycle, or wrong content). Gemini is used only when both face and motorcycle are missing. Example:

```json
{
  "message": "Your Profile Verification Failed\nIt looks like your photo shows a flower. Please upload a clear photo that shows both your face and your motorcycle.",
  "emailSent": true
}
```

The email includes a header, subtitle, verification message, and a link to the submitted photo URL. If email delivery fails after analysis and message generation succeed, the endpoint returns **502 Bad Gateway**.

## Project structure

```
src/Pentagon.Functions/
├── Functions/
│   ├── ProcessImageFunction.cs         # Technical analysis HTTP trigger
│   ├── VerifyImageFunction.cs          # User-facing verification message HTTP trigger
│   └── ProcessImageRequestExtensions.cs
├── Models/                             # Request/response DTOs
├── Services/                           # Vision, Gemini, email, and Key Vault integration
├── Program.cs                          # App entry point
├── host.json                           # Functions host config
└── local.settings.json.example         # Local dev settings template
```

## Possible improvements

- Add Agent Validation 
- Add Feedback Validation Mechanism that continues to improve on the result