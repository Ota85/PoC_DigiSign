# PoC_DigiSign

Proof of Concept for the [DigiSign Identify](https://www.digisign.cz/) integration.

The application demonstrates the DigiSign Identify provider flow:

1. User clicks **Start Verification** on the home page.
2. The app calls `POST /api/identifications` to create a new identification (with `scenarioId` and `name`).
3. The app calls `POST /api/identifications/{id}/start` to get the verification URL (with `redirectUrl` and optional `validityMinutes`).
4. The browser opens the DigiSign verification page in a **new tab** (not an iframe; DigiSign does not support iframe embedding for document verification).
5. After verification DigiSign redirects the user back to `/Callback`, which displays the result status and all returned query parameters.

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
    "ScenarioId": "",
    "Name": "PoC Verification",
    "RedirectUrl": "",
    "ValidityMinutes": 0
  }
}
```

| Key | Required | Description |
|-----|----------|-------------|
| `BaseUrl` | yes | DigiSign API base URL |
| `BearerToken` | yes | API bearer token from the DigiSign portal |
| `ScenarioId` | yes | ID of the verification scenario to use |
| `Name` | no | Display name sent with the identification (default: `PoC Verification`) |
| `RedirectUrl` | no | Absolute callback URL; if empty, auto-built from the incoming request host |
| `ValidityMinutes` | no | Requested validity of the start URL in minutes; `0` = provider default (5 min) |

Store secrets locally using the .NET User Secrets tool (never commit real tokens):

```bash
cd DigiSignPoC
dotnet user-secrets set "DigiSign:BearerToken" "<your-bearer-token>"
dotnet user-secrets set "DigiSign:ScenarioId" "<your-scenario-id>"
```

Alternatively use environment variables (double underscore maps to nested config keys):

```bash
DigiSign__BearerToken="<token>" DigiSign__ScenarioId="<id>" dotnet run
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
    ├── Index.cshtml            # Start verification button; shows identification ID and opens URL in new tab
    ├── Index.cshtml.cs         # Calls DigiSign API (create + start), exposes result to view
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

---

## Technical record

### Verified request/response flow

**Step 1 – Create identification**

```
POST /api/identifications
Authorization: ******
Content-Type: application/json

{ "scenarioId": "<id>", "name": "PoC Verification" }

→ 201 { "id": "<identification-id>", ... }
```

**Step 2 – Start identification**

```
POST /api/identifications/<identification-id>/start
Authorization: ******
Content-Type: application/json

{ "redirectUrl": "https://<host>/Callback" }
  // optionally: "validityMinutes": <n>

→ 200 { "url": "https://verify.digisign.cz/...", ... }
```

**Step 3 – User completes verification on DigiSign page**

DigiSign redirects back to the configured `redirectUrl` with query parameters, e.g.:

```
GET /Callback?status=success&identificationId=<id>
```

The callback page displays the `status` value and all returned query parameters.

### New-tab / new-window constraint

DigiSign does not support iframe embedding for document-based verification. The app opens the verification URL via `window.open(url, '_blank')` triggered on page load after the start call. Note that **browser popup blockers may suppress this**; the page always includes a visible fallback link so the user can open the URL manually.

### Required configuration values

| Value | Where to find it |
|-------|-----------------|
| `BearerToken` | DigiSign portal → API / Integration settings → generate/copy token |
| `ScenarioId` | DigiSign portal → Scenarios → open scenario → copy ID (UUID) |

### Open questions for production implementation

1. **Server-side result verification** – the callback `?status=` query parameter is not cryptographically signed. Production code should call `GET /api/identifications/{id}` to fetch the authoritative result from DigiSign's server instead of trusting the redirect parameter.
2. **User account linking** – the PoC has no concept of sessions or user accounts. Production must associate the `identificationId` with the logged-in user before redirecting and verify it matches on callback.
3. **Result persistence** – verification results should be stored in a database.
4. **`validityMinutes` sandbox support** – needs to be confirmed with DigiSign whether the sandbox environment honours this field on the start endpoint.
5. **HTTPS requirement** – DigiSign requires an HTTPS `redirectUrl`. Local development may need a trusted dev certificate (`dotnet dev-certs https --trust`).
