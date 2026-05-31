# Pentagon

Azure Functions app scaffold for processing image submissions.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Azure Functions Core Tools v4](https://learn.microsoft.com/en-us/azure/azure-functions/functions-run-local)
- [Azurite](https://learn.microsoft.com/en-us/azure/storage/common/storage-use-azurite) (optional, for local storage emulation)

## Getting started

1. Clone the repository.
2. Copy the local settings template:

   ```bash
   cp src/Pentagon.Functions/local.settings.json.example src/Pentagon.Functions/local.settings.json
   ```

3. Build and run from the project directory:

   ```bash
   cd src/Pentagon.Functions
   dotnet build
   func start
   ```

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
| `imageUrl` | Yes      | URL of the image to process |
| `email`    | No       | Contact email address    |
| `phone`    | No       | Contact phone number     |

The function is intentionally unimplemented. Requests will return a not-implemented response until business logic is added.

## Project structure

```
src/Pentagon.Functions/
├── Functions/ProcessImageFunction.cs   # HTTP trigger stub
├── Models/ProcessImageRequest.cs       # Request DTO
├── Program.cs                          # App entry point
├── host.json                           # Functions host config
└── local.settings.json.example         # Local dev settings template
```