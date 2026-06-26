# PoC_DigiSign

Proof of Concept for the [DigiSign Identify](https://www.digisign.cz/) integration.

The application demonstrates the DigiSign Identify provider flow:

1. User clicks **Start Verification** on the home page.
2. The app calls `POST /api/identifications` to create a new identification.
3. The app calls `POST /api/identifications/{id}/start` to get the verification URL.
4. The browser is redirected to the DigiSign verification page (top-level, not inside an iframe).
5. After verification DigiSign redirects the user back to `/Callback`, which displays the result.

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A DigiSign Identify account with:
  - A bearer token
  - A configured verification scenario (scenario ID)

---

## Configuration

The application reads configuration from `appsettings.json` and [User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets).

`appsettings.json` contains non-secret defaults:

```json
{
  "DigiSign": {
    "BaseUrl": "https://api.digisign.org",
    "BearerToken": "",
    "ScenarioId": ""
  }
}
```

Store secrets locally using the .NET User Secrets tool (never commit real tokens):

```bash
cd DigiSignPoC
dotnet user-secrets set "DigiSign:BearerToken" "<your-bearer-token>"
dotnet user-secrets set "DigiSign:ScenarioId" "<your-scenario-id>"
```

---

## Running the application

```bash
cd DigiSignPoC
dotnet run
```

Open the URL printed in the console (e.g. `https://localhost:5001`) and click **Start Verification**.

---

## Project structure

```
DigiSignPoC/
├── Program.cs                  # App entry point, HttpClient registration
├── appsettings.json            # Non-secret configuration
├── DigiSignPoC.csproj
└── Pages/
    ├── Index.cshtml            # Start verification button
    ├── Index.cshtml.cs         # Calls DigiSign API, redirects to verification URL
    ├── Callback.cshtml         # Shows result after DigiSign redirects back
    ├── Callback.cshtml.cs      # Reads and logs callback query parameters
    ├── Shared/
    │   └── _Layout.cshtml      # Minimal Bootstrap layout
    ├── _ViewImports.cshtml
    └── _ViewStart.cshtml
```

---

## Logging

Set `"DigiSignPoC": "Debug"` in `appsettings.json` (already the default) to see detailed request/response logs from the DigiSign API calls.
