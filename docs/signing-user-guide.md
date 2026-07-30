# DigiSign PoC - Document Signing User Manual

| Document field | Value |
|---|---|
| Audience | PoC operators, demonstrators, testers, and signing recipients |
| Application | DigiSign integration PoC |
| Scope | One PDF, one recipient, one signature field |
| Supported signing methods | Bank iD SIGN; DigiSign Identify followed by a simple signature |
| Guide version | 1.0 |
| Last reviewed | 29 July 2026 |
| Document status | Controlled PoC operating guide |

## 1. Purpose

This guide explains how to prepare, start, complete, and verify a document-signing journey in the
DigiSign PoC.

This is a production-grade guide for operating and assessing the PoC. It does not state that the
PoC application itself is production-ready. Section 17 identifies the controls and components that
would still be required in a production implementation.

The PoC separates document signing from standalone identity verification. From the home page, the
operator chooses either:

- **Identify a person** for standalone KYC-style identity verification; or
- **Sign a document** for the envelope-based signing workflow described in this guide.

The signing workflow always creates a new DigiSign envelope. The two supported choices are:

- **Bank iD SIGN** — the recipient signs through a supported bank;
- **DigiSign Identify + simple signature** — the recipient completes a new, envelope-linked
  document/selfie verification before applying a simple signature.

The PoC does not attach an earlier standalone identification to the envelope.

The documented provider behavior and API sequence were checked against the official DigiSign REST
API, embedded signing, recipient configuration, DigiSign Identify, and webhook guidance listed in
section 18.

## 2. Roles

### PoC operator

The operator:

- configures the shared DigiSign credentials;
- selects the document and signing method;
- enters recipient details;
- chooses the signature position;
- creates the envelope;
- opens or delivers the signing journey;
- checks the final status and downloads the result.

### Signing recipient

The recipient:

- authenticates with Bank iD or DigiSign Identify;
- reviews the document in DigiSign;
- confirms the signature;
- returns to the PoC result page.

During a demonstration, the operator and recipient may be the same person using the same browser.

### DigiSign workspace administrator

The administrator prepares the DigiSign workspace before testing:

- enables the API module;
- creates API credentials;
- enables Bank iD SIGN when that route is required;
- enables DigiSign Identify and creates at least one Identify scenario;
- optionally enables suppression of recipient invitation emails.

## 3. Before you begin

### 3.1 Required access

Confirm all of the following:

- the PoC is running;
- you can open its home page;
- the intended DigiSign environment is available;
- you have a valid bearer token or DigiSign `accessKey` and `secretKey`;
- the callback URL routes to the same running PoC;
- the browser permits cookies and popups for the PoC;
- Bank iD SIGN and/or DigiSign Identify is enabled in the selected DigiSign workspace.

Do not mix staging credentials with the production API URL.

### 3.2 Document requirements

Prepare a document that:

- is a PDF;
- is not empty;
- is 25 MB or smaller;
- contains the page selected for the signature field;
- does not contain information that is inappropriate for the selected testing environment.

Use a disposable test PDF in staging. DigiSign staging certificates are not valid production
certificates, and staging timestamps are not qualified.

### 3.3 Recipient information

Have the following available:

- the recipient's full name;
- a valid email address;
- a mobile number if the selected provider configuration requires one;
- access to a supported Bank iD for Bank iD SIGN; or
- suitable identity documents and a camera-equipped device for DigiSign Identify.

The name entered in the PoC is particularly important for Identify signing. The current PoC asks
DigiSign to validate the recipient's name against the name obtained during Identify.

## 4. User journey at a glance

```text
Configure shared credentials
        ↓
Choose "Sign a document"
        ↓
Prepare PDF, recipient, signing method, and signature position
        ↓
Create and send DigiSign envelope
        ↓
Open embedded signing popup
        ↓
Bank iD SIGN ──────── or ──────── DigiSign Identify + simple signature
        ↓                                  ↓
Authenticate and sign              Scan/verify identity, then sign
        └──────────────────┬───────────────┘
                           ↓
                 Return to the PoC
                           ↓
             Confirm authoritative status
                           ↓
          Download signed PDF and audit log
```

| Signing route | Recipient experience | Identity-document scan | PoC recipient configuration |
|---|---|---|---|
| Bank iD SIGN | Continue to a supported bank, authenticate, authorize, return | No | `signatureType = bank_id_sign` |
| DigiSign Identify + simple signature | Complete the selected Identify scenario, return to the document, apply simple signature | Normally yes; determined by the scenario | `signatureType = simple`; `authenticationOnSignature = identify` |

In both routes, the signing window returns to the PoC callback and the PoC retrieves the envelope
from DigiSign. A successful browser return alone is not treated as proof of a completed envelope.

## 5. Configure shared DigiSign credentials

Credentials are configured once and used by both PoC workflows.

1. Open the PoC home page.
2. Select **Manage credentials**.
3. Under **DigiSign environment**, choose:

   - **Staging** for tests; or
   - **Production** only for an explicitly authorized production test.

4. Provide one authentication option:

   - paste an existing bearer token; or
   - enter both `accessKey` and `secretKey`.

5. Select **Save and validate credentials**.
6. Confirm the green **Authentication configured** message.
7. Check that the displayed API environment is the intended one.
8. Return to **Back to workflows**.

When API keys are used, the PoC obtains a bearer token and refreshes it when it is close to
expiration. The bearer token and selected environment are saved in
`DigiSignPoC/App_Data/digisign-auth.json` and loaded on the next application start. API keys entered
through the page remain in memory only.

> **PoC security notice:** The bearer token is stored as plain JSON and shared by every user of the
> PoC instance. Do not expose this PoC publicly or treat its credential storage as production-ready.

## 6. Open the signing workflow

1. On the home page, select **Open signing workflow** under **Sign a document**.
2. Confirm that **Shared authentication: bearer token validated** is shown in green.
3. Confirm that the displayed API URL is correct.

Opening the signing or standalone Identify page validates the saved bearer token through
`GET /api/account/me`. If DigiSign rejects the token, an update-token warning appears at the top of
the page and workflow actions remain unavailable. Select **Update DigiSign credentials** and
provide either:

- a new bearer token; or
- the DigiSign `accessKey` and `secretKey`, which the PoC exchanges for a new bearer token.

If DigiSign cannot be reached, the page shows a warning instead of treating the token as rejected.

## 7. Complete the signing form

### 7.1 Signing and verification scenario

Under **1. Signing and verification scenario**, choose exactly one route.

#### Option A (default): DigiSign Identify + simple signature

Select **DigiSign Identify + simple signature** when:

- a new document/selfie verification must be connected to this signing envelope;
- the recipient cannot or should not use Bank iD SIGN;
- the workspace has an active Identify scenario.

Select **Load Identify scenarios**, then select or enter the required **Identify scenario**. The
scenario determines which documents, selfie/liveness steps, and approval mode DigiSign requires.

Loading scenarios submits and reloads the page. The document selector is intentionally last on the
page, after scenario selection, so the browser does not clear an already selected PDF during this
reload.

You may also enter a known Identify scenario UUID directly without loading the list. For a
time-bounded demonstration, prefer an automatic Identify scenario. A manual-approval scenario may
pause the journey until a DigiSign administrator reviews it.

#### Option B: Bank iD SIGN

Select **Bank iD SIGN** when:

- the workspace has Bank iD SIGN enabled;
- the recipient has access to a supported bank;
- an identity-bearing Bank iD signature is desired;
- you want to avoid document scanning.

The PoC configures the DigiSign recipient with the `bank_id_sign` signature type.

### 7.2 Signature placement

Under **2. Signature placement**:

1. Enter the **Page** number. Pages start at `1`.
2. Enter the **X coordinate**.
3. Enter the **Y coordinate**.

Coordinates are PDF points measured from the top-left corner:

- 72 points = 1 inch;
- an A4 page is approximately 595 × 842 points;
- DigiSign documents the visible signature mark as approximately 55 × 21 mm, so leave enough space
  to the right and below the selected point.

The defaults are page `1`, X `72`, Y `650`. These are only a convenient starting point. The PoC
does not preview the field before sending, so use a known PDF and verify the coordinates carefully.

For repeatable production documents, use a DigiSign template or a unique signature placeholder
instead of manual coordinates.

### 7.3 Advanced callback settings

Most users should keep **Advanced callback settings** unchanged.

- **Signing callback URL** must be an absolute URL that reaches `/SigningCallback` on the running
  PoC.
- **Embed-link validity** defaults to 15 minutes. It may be `0` for DigiSign's 5-minute default,
  or an explicit value between 1 and 60 minutes.

Changing the environment or restarting the PoC after the envelope is created can invalidate the
local correlation needed by the callback.

### 7.4 Personal information

Under **3. Personal information**:

1. Enter the recipient's legal or expected **Full name**.
2. Enter a valid **Email**.
3. Enter **Mobile number** when required by the workspace or selected signing method. Use an
   international format such as `+420...`.
4. Decide whether to select:

   **Suppress DigiSign's invitation email and use only the embedded PoC link**

   - Leave it cleared for the most compatible test. DigiSign may send its normal invitation email.
   - Select it for an embedded-only demonstration.
   - Suppression works only when the DigiSign workspace administrator has enabled the corresponding
     advanced option.

The PoC still requests email delivery of the completed document link.

### 7.5 Document and envelope

Under **4. Document and envelope**, the final form section:

1. Select **PDF document** and choose the PDF.
2. Review **Envelope name**.

   The name identifies the envelope in DigiSign and is also used as the email subject.

3. Review **Message to signer**.

   DigiSign uses this text in its recipient communication when provider email delivery is enabled.

## 8. Create the envelope

1. Review all form values.
2. Select **Create envelope and start signing** once.
3. Keep the browser tab open while the PoC prepares the envelope.

The PoC now:

1. creates the envelope;
2. uploads the PDF;
3. attaches the PDF as a signable document;
4. adds the recipient;
5. creates one signature field;
6. sends the envelope;
7. waits for DigiSign's asynchronous preparation;
8. creates an embedded signing URL.

When preparation succeeds, the page shows:

- **Envelope ID**;
- **Document ID**;
- **Recipient ID**;
- **Open DigiSign signing**.

Keep these IDs when reporting a problem.

If preparation fails after envelope creation, the error panel displays the partially created
envelope ID. Do not repeatedly select the submit button, because every successful create attempt
produces another DigiSign envelope.

## 9. Open the signing session

1. Select **Open DigiSign signing**.
2. Allow the popup if the browser requests permission.
3. Keep the original PoC tab open.

The main tab displays **Signing in progress** and checks its local session once per second for up to
30 minutes. It does not continuously poll DigiSign.

If the popup is blocked:

1. allow popups for the PoC;
2. select **Open DigiSign manually** in the warning;
3. do not close the original PoC tab.

Do not open the signing URL in multiple windows or share it with unintended recipients. It is a
short-lived access link to the signing ceremony.

## 10. Complete Bank iD SIGN

The exact screens depend on DigiSign, Bank iD, and the selected bank.

The typical recipient journey is:

1. Review the DigiSign welcome and document information.
2. Select or continue with Bank iD SIGN.
3. Choose the supported bank if prompted.
4. Continue to the bank's authentication page or application.
5. Authenticate using the bank's required method.
6. Review the signature authorization presented by the bank.
7. Confirm the signature.
8. Return to DigiSign.
9. Confirm that DigiSign reports successful signing.

After success, DigiSign redirects the popup to the PoC callback. The popup normally closes
automatically and the original tab opens **Signing result**.

If the recipient cancels at the bank, fails authentication, or the bank does not support the
required operation, DigiSign may return an error or leave the envelope incomplete.

## 11. Complete DigiSign Identify signing

The exact verification screens depend on the configured Identify scenario.

The typical recipient journey is:

1. Review the DigiSign welcome and document information.
2. Start DigiSign Identify when prompted before signing.
3. Review and accept the required privacy/consent information.
4. Choose the requested identity document type.
5. Photograph or upload the required side or sides of the primary document.
6. Supply a secondary document if the scenario requires it.
7. Complete selfie and liveness steps if required.
8. Submit the identification.
9. Wait for the scenario result:

   - an automatic scenario normally approves or denies immediately;
   - a manual scenario waits for administrator review.

10. If approved, return to the document and review it.
11. Select the signature field and confirm the simple signature.
12. Confirm successful completion.

The PoC asks DigiSign to compare the entered recipient name with the name obtained by Identify. A
mismatch prevents completion and may return an `identificationClaimsMismatch` error.

After success, DigiSign redirects the popup to the callback. The popup normally closes
automatically and the original tab opens **Signing result**.

## 12. Review the signing result

The result page loads the authoritative envelope status from DigiSign during the callback.

### Status and action

| Displayed status | Meaning | Operator action |
|---|---|---|
| `completed` | All required signing actions are complete | Download and inspect the signed output |
| `sent` | Envelope was sent but is not complete | Select **Refresh envelope result** after the recipient finishes |
| `declined` | A signer declined | Review the provider response and start a new envelope only if appropriate |
| `disapproved` | An approver rejected the envelope | Review the reason and business process |
| `expired` | The envelope validity ended | Create and send a new envelope |
| `cancelled` or `withdrawn` | The envelope was stopped | Confirm why it was stopped before retrying |
| no status or error | The authoritative response could not be loaded | Check credentials, environment, callback, and provider details |

The page also displays:

- envelope, recipient, and document IDs;
- selected signing method;
- callback event;
- callback error, when present;
- the complete DigiSign envelope response under an expandable diagnostic section.

Treat the authoritative envelope status as the result. Callback query parameters are useful for
the user interface and troubleshooting but are not sufficient proof of completion.

## 13. Download and verify the output

When the status is `completed`:

1. Select **Download signed PDF and audit log**.
2. Save the downloaded file to an approved test location.
3. Open the PDF.
4. Confirm:

   - the correct document was signed;
   - the signature appears on the intended page and position;
   - the recipient identity/name is correct;
   - the DigiSign audit information is present;
   - the PDF viewer reports the expected certificate status for the environment.

Staging certificates are expected not to validate as production certificates.

Do not treat a browser success page alone as proof that the envelope completed. Retain the
downloaded result, audit information, and envelope ID according to the test plan.

## 14. Start another workflow

- Select **Start a new signing flow** to prepare another document.
- Select **Choose another workflow** to return to the home page and choose standalone Identify.

Starting another signing flow replaces the active signing correlation in the current browser
session. Complete or record the current result first.

## 15. Troubleshooting

| Symptom | Likely cause | Resolution |
|---|---|---|
| Authentication is not configured | No bearer token was saved or the saved token is unusable | Open **Manage credentials** and configure them again |
| Credentials are rejected | Wrong keys, expired token, or environment mismatch | Match staging keys to staging and production keys to production |
| Identify scenarios cannot be loaded | Identify is unavailable, credentials lack access, or no scenario exists | Check the workspace and create/activate a scenario |
| Selected PDF disappeared | The scenarios button reloaded the form | Load scenarios first, then select the PDF again |
| Bank iD SIGN recipient cannot be created | Bank iD SIGN is not enabled or workspace configuration is incompatible | Ask the DigiSign administrator to confirm Bank iD SIGN availability |
| Identify recipient cannot be created | Scenario ID is invalid or Identify is not enabled | Load the available scenarios and select an active one |
| Invitation suppression is rejected | Advanced email-suppression option is disabled | Clear the checkbox or enable the option in DigiSign |
| Signature field is misplaced | Incorrect page or coordinates | Correct the coordinates and create a new envelope |
| Envelope is created but embed URL fails | DigiSign is still preparing the asynchronously sent envelope or recipient settings are invalid | Wait for the PoC retries; use the displayed envelope ID to inspect DigiSign |
| Popup is blocked | Browser popup policy | Allow popups and use **Open DigiSign manually** |
| Callback does not match active flow | Session cookie lost, app restarted, environment changed, or another signing flow replaced it | Restore credentials if needed and start a new signing flow |
| Signing progress reaches 30 minutes | No callback reached the original session | Inspect the envelope in DigiSign and verify callback routing |
| Result is not `completed` | Recipient has not finished or the envelope needs manual processing | Complete the provider journey, then select **Refresh envelope result** |
| Download is unavailable | Envelope is not `completed` | Refresh after successful completion |
| Download fails after restart | Browser session state was lost, or the saved bearer token expired | Start a new flow or reconfigure credentials; use DigiSign administration to retrieve the envelope |

## 16. Safe demonstration checklist

Before the demonstration:

- [ ] Confirm the intended staging/production environment.
- [ ] Configure credentials and confirm the green status.
- [ ] Confirm callback routing.
- [ ] Enable the required signing module.
- [ ] For Identify, activate an appropriate scenario.
- [ ] Prepare a disposable PDF of 25 MB or less.
- [ ] Confirm signature page and coordinates.
- [ ] Allow popups.

After the demonstration:

- [ ] Confirm authoritative status `completed`.
- [ ] Download and inspect the signed PDF and audit log.
- [ ] Record the envelope ID in the test evidence.
- [ ] Remove test envelopes/documents according to the agreed retention policy.
- [ ] Remove `DigiSignPoC/App_Data/digisign-auth.json` when the saved PoC token is no longer needed.

## 17. PoC limitations

This guide is operationally complete for the implemented PoC, but the application itself is not
production-ready:

- the bearer token is global and stored as plain JSON;
- API keys entered through the UI are global and held in memory;
- browser session state is not persisted;
- only one active signing flow is retained per browser session;
- callbacks require the same browser session;
- no webhook processing is implemented;
- no malware scanning or content inspection is implemented;
- no PDF preview or signature-position preview is implemented;
- there is no authentication or authorization around the PoC pages;
- there is no durable audit database;
- one PDF, one recipient, and one signature field are supported.

A production implementation must add scoped access control, managed secrets, persistent state,
signed and idempotent webhooks, secure document storage, retention/deletion controls, file
validation, malware scanning, monitoring, audit access, and privacy controls.

## 18. Reference documentation

- [DigiSign basic REST API envelope flow](https://help.digisign.org/cs/articles/9766040-zakladni-pouziti-rest-api)
- [DigiSign embedded signing](https://help.digisign.org/cs/articles/9842671-embedovani-podpisu)
- [Recipient verification and signature types](https://help.digisign.org/cs/articles/9925242-nastaveni-prijemcu-typu-podpisu-a-podpisoveho-workflow)
- [DigiSign Identify in an envelope](https://help.digisign.org/cs/articles/11992663-pouziti-digisign-identify-scenare-pro-overeni-prijemce-v-obalce)
- [DigiSign webhook technical specification](https://help.digisign.org/cs/articles/9766606-webhooky-technicka-specifikace-a-sprava-pres-api)
- [DigiSign OpenAPI documentation](https://api.digisign.org/api/docs)

## 19. API and browser flow reference

The PoC performs the following server-to-server operations after the operator selects **Create
envelope and start signing**:

| Step | Request | Purpose | ID retained by the PoC |
|---|---|---|---|
| 1 | `POST /api/auth-token` | Exchange API keys for a bearer token when required | Token expiry |
| 2 | `POST /api/envelopes` | Create the draft envelope | Envelope ID |
| 3 | `POST /api/files` | Upload the PDF as multipart form data | File ID |
| 4 | `POST /api/envelopes/{envelopeId}/documents` | Attach the PDF as a signable document | Document ID |
| 5 | `POST /api/envelopes/{envelopeId}/recipients` | Add the signer and signing/verification configuration | Recipient ID |
| 6 | `POST /api/envelopes/{envelopeId}/tags` | Place one signature field | Tag response |
| 7 | `POST /api/envelopes/{envelopeId}/send` | Validate and send the envelope | Envelope becomes active |
| 8 | `POST /api/envelopes/{envelopeId}/recipients/{recipientId}/embed` | Create the short-lived signing URL | Embedded URL |

The browser then follows this sequence:

```text
PoC signing page
    |
    | open short-lived embedded URL
    v
DigiSign signing window
    |
    | Bank iD SIGN, or Identify followed by simple signature
    v
Configured returnUrl/failureUrl
    |
    | GET /SigningCallback?signingFlow=...&event=... or &error=...
    v
PoC callback
    |
    | GET /api/envelopes/{envelopeId}
    v
Authoritative signing result
    |
    | completed only
    v
GET /api/envelopes/{envelopeId}/download
```

The callback query parameters drive navigation and diagnostics. The envelope retrieved from
DigiSign is the authoritative status. The PoC requests a combined download containing the signable
document and audit log after status `completed`.

The embedded URL is short-lived. The PoC defaults to an explicit validity of 15 minutes. It accepts
`0` to omit the expiration and use DigiSign's 5-minute default, or another explicit validity from
1 to 60 minutes.

The PoC retries creation of the embedded URL for up to 20 attempts with a two-second interval when
DigiSign temporarily returns HTTP 400, 409, or 422 while preparing the sent envelope.

## 20. PoC acceptance test

Use this test to collect repeatable evidence that the implemented signing journey works in the
selected DigiSign environment.

### 20.1 Preparation

1. Record the PoC build or commit identifier.
2. Record the DigiSign environment and workspace.
3. Confirm that no real production personal data is present in a staging test.
4. Prepare a disposable PDF with a known blank signature area.
5. Confirm callback routing and allow browser popups.
6. Configure credentials and record only whether validation succeeded. Never copy secrets into the
   evidence.

### 20.2 Bank iD SIGN test

1. Create an envelope using **Bank iD SIGN**.
2. Record the envelope, document, and recipient IDs.
3. Complete bank authentication and signature authorization.
4. Confirm that the popup returns to the PoC.
5. Confirm authoritative envelope status `completed`.
6. Download and inspect the signed PDF and audit log.
7. Record pass/fail, timestamp, browser, and any provider error.

### 20.3 DigiSign Identify signing test

1. Load and record the selected Identify scenario name and ID.
2. Create an envelope using **DigiSign Identify + simple signature**.
3. Record the envelope, document, and recipient IDs.
4. Complete every document, selfie, liveness, and consent step required by the scenario.
5. Confirm that the verified name matches the signer name entered by the operator.
6. Apply the simple signature and return to the PoC.
7. Confirm authoritative envelope status `completed`.
8. Download and inspect the signed PDF and audit log.
9. Record pass/fail, timestamp, browser, and any provider error.

### 20.4 Negative checks

- Use an invalid or expired token and confirm that the workflow is blocked with a clear error.
- Select a non-PDF file and confirm that the PoC rejects it.
- Use a PDF larger than 25 MB and confirm that the PoC rejects it.
- Enter an invalid signer email and confirm that the PoC rejects it.
- For Identify, omit the scenario and confirm that the PoC rejects the request.
- Allow an embedded URL to expire and confirm that a new signing flow is required.
- Close or isolate the browser session and confirm that the PoC does not incorrectly associate a
  callback with another session.

**Test coverage note.** The repository has no automated tests; this is the documented PoC
verification. Production must automate payload, validation, callback, webhook signature and
idempotency, authorization, and failure-path tests.
