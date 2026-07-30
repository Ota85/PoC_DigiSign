# DigiSign integration PoC

Small ASP.NET Core application for exercising two independent DigiSign journeys:

1. standalone identity verification with **DigiSign Identify**;
2. PDF document signing with either **Bank iD SIGN** or a **simple signature protected by a new DigiSign Identify verification**.

The landing page lets the user choose the workflow. Authentication is configured once and shared by
both workflows. The bearer token is persisted locally for reuse after an application restart.

This is a technical PoC, not a production identity or document-signing application.

## User documentation

- [Document Signing User Manual (PDF)](output/pdf/DigiSign-PoC-Signing-User-Manual.pdf) - controlled,
  publication-ready operator and recipient guidance.
- [Document Signing User Manual (editable source)](docs/signing-user-guide.md) - maintained content
  for Bank iD SIGN and DigiSign Identify signing.

## Prerequisites

- .NET 10 SDK
- DigiSign staging or production account with the API module
- DigiSign `accessKey` and `secretKey`, or an existing bearer JWT
- DigiSign Identify module and at least one Identify scenario
- Bank iD SIGN enabled in the DigiSign workspace to test the Bank iD signing option
- a public HTTPS callback address that forwards to the running application

The free staging environment can be requested from DigiSign support. Staging API keys and data are
separate from production.

## Run

```powershell
cd DigiSignPoC
dotnet run --launch-profile http
```

Open `http://localhost:5000`.

## Shared authentication

Open **Credentials** and enter:

- a DigiSign API base URL;
- either a bearer JWT;
- or `accessKey` and `secretKey`.

When API keys are supplied, the PoC exchanges them at `POST /api/auth-token`, caches and persists
the token, and automatically refreshes it shortly before expiration.

Credentials and tokens:

- keep the bearer token, environment, and token timestamps in
  `DigiSignPoC/App_Data/digisign-auth.json`;
- load the saved bearer token when the application restarts;
- keep API keys entered through the UI only in the ASP.NET process;
- are never stored in the browser session;
- are shared by all users of this PoC process.

The local token file is excluded from Git. This deliberately simple model is suitable only for a
single-user PoC. A production application must use a secret store, scoped credentials, persistent
workflow state, and appropriate access controls.

Non-secret defaults are stored in `DigiSignPoC/appsettings.json`:

```json
{
  "DigiSign": {
    "BaseUrl": "https://api.staging.digisign.org",
    "BearerToken": "",
    "AccessKey": "",
    "ScenarioId": "",
    "Name": "PoC Verification",
    "RedirectUrl": "https://sign.revolving.dev.linksoft.cz/Callback",
    "SigningRedirectUrl": "https://sign.revolving.dev.linksoft.cz/SigningCallback",
    "LinkExpiration": 0
  }
}
```

Secrets may still be supplied through .NET User Secrets:

```powershell
cd DigiSignPoC
dotnet user-secrets set "DigiSign:AccessKey" "<access-key>"
dotnet user-secrets set "DigiSign:SecretKey" "<secret-key>"
```

## Workflow 1: standalone Identify

1. Open **Identify**.
2. Load or enter an Identify scenario.
3. Create an identification:

   ```http
   POST /api/identifications
   ```

4. Start it:

   ```http
   POST /api/identifications/{identificationId}/start
   ```

5. Open the returned `identifyUrl` in a popup.
6. DigiSign returns to `/Callback`.
7. The callback loads the authoritative result:

   ```http
   GET /api/identifications/{identificationId}
   ```

8. The main page displays the status and complete provider response.

The callback query string is displayed for diagnostics but is not treated as the authoritative
verification result.

## Workflow 2: document signing

1. Open **Sign document**.
2. Select a PDF, enter signer data, and select:

   - **Bank iD SIGN**; or
   - **DigiSign Identify + simple signature**.

3. The PoC creates the signing resources in this order:

   ```text
   POST /api/envelopes
   POST /api/files
   POST /api/envelopes/{envelopeId}/documents
   POST /api/envelopes/{envelopeId}/recipients
   POST /api/envelopes/{envelopeId}/tags
   POST /api/envelopes/{envelopeId}/send
   POST /api/envelopes/{envelopeId}/recipients/{recipientId}/embed
   ```

4. The embedded URL opens in a popup.
5. DigiSign returns to `/SigningCallback`.
6. The callback loads the authoritative envelope:

   ```http
   GET /api/envelopes/{envelopeId}
   ```

7. When the status is `completed`, the result page can download the signed PDF and audit log:

   ```http
   GET /api/envelopes/{envelopeId}/download
   ```

### Bank iD SIGN recipient

The Bank iD path configures the recipient with `signatureType = bank_id_sign`. Availability depends
on the DigiSign workspace and the signer having a supported Bank iD.

### Identify-protected recipient

The Identify path configures:

```json
{
  "signatureType": "simple",
  "authenticationOnSignature": "identify",
  "identifyScenario": "<scenario-id>",
  "identifyValidatedFields": ["name"]
}
```

This intentionally creates a new envelope-linked identification. DigiSign's public API does not
document attaching the standalone identification from workflow 1 to a later envelope.

### Invitation delivery

By default, DigiSign may also send its normal invitation email. The UI can request
`channelForSigner = none` so that only the embedded PoC URL is used, but the corresponding advanced
option must first be enabled in the DigiSign workspace.

### Signature placement

The PoC places one signature tag using a page number and X/Y coordinates. Coordinates are PDF
points measured from the top-left corner, with 72 points per inch.

For generated production documents, a unique text placeholder or an envelope template is usually
more reliable than user-entered coordinates.

## PoC limitations and production work

- Authentication is global, in memory, and intended for one PoC operator.
- Workflow state and results are stored in ASP.NET in-memory session.
- Only one active Identify flow and one active signing flow are retained per browser session.
- Only PDF upload and one signer/signature field are implemented.
- The callback depends on the same browser session and is not a reliable asynchronous completion
  mechanism.
- Production should process signed and idempotent DigiSign webhooks, especially
  `recipientSigned`, `envelopeCompleted`, decline, cancellation, and expiration events.
- Production must persist correlations among application user, identification, envelope, recipient,
  and document.
- File validation, malware scanning, size policy, retention, deletion, authorization, audit access,
  and GDPR rules remain production responsibilities.
- The staging environment uses non-valid signing certificates and non-qualified timestamps.

## DigiSign documentation

- [Basic REST API envelope flow](https://help.digisign.org/cs/articles/9766040-zakladni-pouziti-rest-api)
- [Embedded signing](https://help.digisign.org/cs/articles/9842671-embedovani-podpisu)
- [Recipient verification and signature types](https://help.digisign.org/cs/articles/9925242-nastaveni-prijemcu-typu-podpisu-a-podpisoveho-workflow)
- [DigiSign Identify in an envelope](https://help.digisign.org/cs/articles/11992663-pouziti-digisign-identify-scenare-pro-overeni-prijemce-v-obalce)
- [Webhook technical specification](https://help.digisign.org/cs/articles/9766606-webhooky-technicka-specifikace-a-sprava-pres-api)
- [OpenAPI documentation](https://api.digisign.org/api/docs)
