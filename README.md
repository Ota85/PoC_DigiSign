# PoC_DigiSign

Small ASP.NET Core Proof of Concept for the standalone DigiSign Identify verification flow.

The purpose is to verify the real provider journey before designing a production business
process:

1. Create an identification with `POST /api/identifications`.
2. Start it with `POST /api/identifications/{id}/start`.
3. Open the returned `identifyUrl` in a new browser tab.
4. Let DigiSign redirect the browser back to `/Callback`.
5. Retrieve the authoritative identification status from DigiSign.
6. Display the result and returned query parameters for technical analysis.

This is a PoC, not a production identity-verification application.

## Prerequisites

- .NET 10 SDK
- DigiSign Identify sandbox or production account
- Bearer JWT, or a DigiSign `accessKey` and `secretKey`
- Configured DigiSign Identify scenario

The selected scenario controls whether DigiSign requests identity documents, a selfie, liveness
checks, and automatic or manual approval.

## Configuration

Non-secret defaults are stored in `DigiSignPoC/appsettings.json`:

```json
{
  "DigiSign": {
    "BaseUrl": "https://api.staging.digisign.org",
    "BearerToken": "",
    "AccessKey": "",
    "ScenarioId": "",
    "Name": "PoC Verification",
    "RedirectUrl": "https://sign.revolving.dev.linksoft.cz/",
    "LinkExpiration": 0
  }
}
```

| Key | Required | Description |
|---|---|---|
| `BaseUrl` | yes | DigiSign API URL. Use the staging URL for sandbox testing. |
| `BearerToken` | optional | Existing bearer JWT used by the DigiSign API. |
| `AccessKey` | optional | API access key used by the UI to obtain a bearer JWT. |
| `ScenarioId` | yes | ID of the Identify scenario configured in DigiSign. |
| `Name` | no | Display name of the identification. |
| `RedirectUrl` | recommended | Absolute callback URL. The default is `https://sign.revolving.dev.linksoft.cz/`. Requests to that host's root are handled by the PoC callback page. |
| `LinkExpiration` | no | Start-link validity in minutes. `0` omits the field and uses the provider default of 5 minutes. |

The `secretKey` is entered only in the UI and is not stored by the PoC. Do not commit real
credentials. Existing values may still be supplied through User Secrets:

```powershell
cd DigiSignPoC
dotnet user-secrets set "DigiSign:BearerToken" "<jwt>"
dotnet user-secrets set "DigiSign:AccessKey" "<access-key>"
dotnet user-secrets set "DigiSign:ScenarioId" "<scenario-id>"
dotnet user-secrets set "DigiSign:RedirectUrl" "https://sign.revolving.dev.linksoft.cz/"
```

Configuration can also be supplied through environment variables such as
`DigiSign__BearerToken` and `DigiSign__ScenarioId`.

## Run

```powershell
cd DigiSignPoC
dotnet run --launch-profile http
```

Open `http://localhost:5000`. The UI supports the complete PoC flow:

1. Select the DigiSign environment.
2. Enter an existing bearer JWT, or enter `accessKey` and `secretKey` and select
   **Get bearer token**.
3. Select **Load available scenarios**. A single returned scenario is selected automatically;
   otherwise select the intended scenario from the list and verify the green selection summary.
4. Review the display name. The public HTTPS callback and provider-default link expiration (`0`) are
   available under **Additional options**.
5. Select **Create and start verification**.

The start button remains disabled until the required authentication, scenario, display name,
callback URL, and link-expiration values are valid.

The browser may block the automatic new tab. The result page always contains a fallback link to
open DigiSign manually.

## Request and response flow

### 1. Create identification

```http
POST /api/identifications
Authorization: Bearer <token>
Content-Type: application/json

{
  "identifyScenario": "<scenario-id>",
  "redirectUrl": "https://sign.revolving.dev.linksoft.cz/",
  "name": "PoC Verification"
}
```

The response contains the provider identification `id`, which the PoC displays and uses in the
next request.

### 2. Start identification

With the provider-default five-minute link validity:

```http
POST /api/identifications/{id}/start
Authorization: Bearer <token>
Content-Type: application/json

{}
```

With a configured longer validity:

```json
{
  "linkExpiration": 15
}
```

The response contains `identifyUrl` and `validTo`. The PoC opens `identifyUrl` in a new tab and
does not use an iframe.

### 3. Return

DigiSign redirects the browser to the configured `redirectUrl`. The PoC stores the identification
ID and bearer JWT in its in-memory session, calls `GET /api/identifications/{id}`, and displays the
authoritative DigiSign status together with any browser query parameters.

Only the API status is treated as the provider result. Browser callback parameters are displayed
for technical analysis only.

## Acceptance criteria coverage

| Criterion | PoC implementation |
|---|---|
| AK-01 | `ScenarioId`, absolute `RedirectUrl`, and `Name` are configurable. |
| AK-02 | Creates the identification and displays its provider ID. |
| AK-03 | Starts the identification and reads `identifyUrl`. |
| AK-04 | Opens `identifyUrl` in a new tab with a manual fallback; no iframe. |
| AK-05 | `/Callback` displays the authoritative API status and browser query parameters. |
| AK-06 | Optional `LinkExpiration`; omitted for the five-minute provider default. |
| AK-07 | Credentials and environment values come from configuration/secrets, not source code. |
| AK-08 | This README records the flow, configuration, limitations, and production questions. |

## Open questions for production

- Correlate the identification ID with the authenticated application user.
- Decide which statuses permit the loan process to continue (`approved`, `for_review`, and so on).
- Persist only the required result data and define retention rules for identity information.
- Consider signed DigiSign webhooks for reliable asynchronous status updates.
- Confirm production authentication/token renewal and the final scenario configuration.
