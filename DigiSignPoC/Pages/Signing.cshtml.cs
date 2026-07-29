using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigiSignPoC.Pages;

[RequestFormLimits(MultipartBodyLengthLimit = 30_000_000)]
[RequestSizeLimit(30_000_000)]
public class SigningModel(
    DigiSignAuthenticationCache authenticationCache,
    IConfiguration configuration,
    ILogger<SigningModel> logger) : PageModel
{
    private const string BankIdSignMethod = "bank_id_sign";
    private const string IdentifyMethod = "identify";

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public DigiSignAuthenticationSnapshot Authentication { get; private set; } =
        authenticationCache.GetSnapshot();

    public string? ErrorMessage { get; private set; }
    public string? ProviderError { get; private set; }
    public string? SuccessMessage { get; private set; }
    public string? EnvelopeId { get; private set; }
    public string? RecipientId { get; private set; }
    public string? DocumentId { get; private set; }
    public string? SigningUrl { get; private set; }
    public string? FlowId { get; private set; }
    public List<IdentifyScenarioOption> IdentifyScenarios { get; private set; } = [];

    public void OnGet()
    {
        Authentication = authenticationCache.GetSnapshot();
        LoadDefaults();
    }

    public IActionResult OnGetSigningState(string? flowId)
    {
        Response.Headers.CacheControl = "no-store, no-cache";

        var flow = DigiSignSigningSession.GetFlow(HttpContext.Session);
        if (string.IsNullOrWhiteSpace(flowId) ||
            flow is null ||
            !string.Equals(flow.FlowId, flowId, StringComparison.Ordinal))
        {
            return new JsonResult(new
            {
                completed = false,
                error = "This signing flow is no longer active."
            })
            {
                StatusCode = StatusCodes.Status409Conflict
            };
        }

        var completion = DigiSignSigningSession.GetCompletion(HttpContext.Session);
        var callbackReceived = completion is not null &&
                               string.Equals(completion.FlowId, flowId, StringComparison.Ordinal);

        return new JsonResult(new
        {
            completed = callbackReceived,
            providerStatus = callbackReceived ? completion!.ProviderStatus : null,
            resultUrl = callbackReceived
                ? Url.Page("/SigningResult", new { flowId })
                : null
        });
    }

    public async Task OnPostLoadIdentifyScenariosAsync()
    {
        Authentication = authenticationCache.GetSnapshot();
        await LoadIdentifyScenariosAsync();
    }

    public async Task OnPostStartAsync()
    {
        Authentication = authenticationCache.GetSnapshot();
        if (!ValidateInput())
        {
            return;
        }

        HttpClient http;
        try
        {
            http = await authenticationCache.CreateAuthenticatedClientAsync(
                cancellationToken: HttpContext.RequestAborted);
        }
        catch (DigiSignAuthenticationException exception)
        {
            ErrorMessage = exception.Message;
            return;
        }

        FlowId = Guid.NewGuid().ToString("N");

        try
        {
            using var envelope = await PostJsonAsync(
                http,
                "api/envelopes",
                new
                {
                    name = Input.EnvelopeName.Trim(),
                    emailSubject = Input.EnvelopeName.Trim(),
                    emailBody = Input.EmailBody.Trim(),
                    emailBodyCompleted = "The signed document is ready for download.",
                    metadata = $"poc-flow:{FlowId}"
                },
                "create the envelope");
            if (envelope is null)
            {
                return;
            }

            EnvelopeId = GetRequiredId(envelope.RootElement, "envelope");

            var fileId = await UploadFileAsync(http, Input.Document!);
            if (fileId is null)
            {
                return;
            }

            using var envelopeDocument = await PostJsonAsync(
                http,
                $"api/envelopes/{EnvelopeId}/documents",
                new
                {
                    name = Path.GetFileName(Input.Document!.FileName),
                    file = fileId,
                    metadata = $"poc-flow:{FlowId}",
                    signable = true
                },
                "add the PDF to the envelope");
            if (envelopeDocument is null)
            {
                return;
            }

            DocumentId = GetRequiredId(envelopeDocument.RootElement, "envelope document");

            var recipientPayload = CreateRecipientPayload();
            using var recipient = await PostJsonAsync(
                http,
                $"api/envelopes/{EnvelopeId}/recipients",
                recipientPayload,
                "add the signer");
            if (recipient is null)
            {
                return;
            }

            RecipientId = GetRequiredId(recipient.RootElement, "recipient");

            using var tag = await PostJsonAsync(
                http,
                $"api/envelopes/{EnvelopeId}/tags",
                new
                {
                    document = $"/api/envelopes/{EnvelopeId}/documents/{DocumentId}",
                    recipient = $"/api/envelopes/{EnvelopeId}/recipients/{RecipientId}",
                    type = "signature",
                    page = Input.SignaturePage,
                    xPosition = Input.SignatureX,
                    yPosition = Input.SignatureY
                },
                "place the signature field");
            if (tag is null)
            {
                return;
            }

            using var sendResponse = await http.PostAsync(
                $"api/envelopes/{EnvelopeId}/send",
                content: null,
                HttpContext.RequestAborted);
            if (!sendResponse.IsSuccessStatusCode)
            {
                await SetProviderFailureAsync(sendResponse, "send the envelope");
                return;
            }

            var signingUrl = await CreateEmbedUrlWithRetryAsync(http);
            if (signingUrl is null)
            {
                return;
            }

            SigningUrl = signingUrl;
            var flow = new DigiSignSigningFlow(
                FlowId,
                Authentication.BaseUrl,
                EnvelopeId,
                RecipientId,
                DocumentId,
                Path.GetFileName(Input.Document.FileName),
                Input.SigningMethod,
                DateTimeOffset.UtcNow);
            DigiSignSigningSession.SetFlow(HttpContext.Session, flow);

            logger.LogInformation(
                "Envelope {EnvelopeId} created for signing flow {FlowId} using {SigningMethod}.",
                EnvelopeId,
                FlowId,
                Input.SigningMethod);
        }
        catch (DigiSignProviderException exception)
        {
            ErrorMessage = exception.Message;
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "DigiSign signing flow {FlowId} failed.", FlowId);
            ErrorMessage = "DigiSign could not be reached while preparing the signing flow.";
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "DigiSign returned invalid JSON for signing flow {FlowId}.", FlowId);
            ErrorMessage = "DigiSign returned an invalid response while preparing the signing flow.";
        }
    }

    private Dictionary<string, object?> CreateRecipientPayload()
    {
        var payload = new Dictionary<string, object?>
        {
            ["role"] = "signer",
            ["name"] = Input.SignerName.Trim(),
            ["email"] = Input.SignerEmail.Trim(),
            ["language"] = "en",
            ["channelForDownload"] = "email",
            ["authenticationOnOpen"] = "none",
            ["authenticationOnDownload"] = "none"
        };

        if (!string.IsNullOrWhiteSpace(Input.SignerMobile))
        {
            payload["mobile"] = Input.SignerMobile.Trim();
        }

        if (Input.SuppressInvitationEmail)
        {
            payload["channelForSigner"] = "none";
        }

        if (Input.SigningMethod == BankIdSignMethod)
        {
            payload["signatureType"] = "bank_id_sign";
            payload["authenticationOnSignature"] = "none";
        }
        else
        {
            payload["signatureType"] = "simple";
            payload["authenticationOnSignature"] = "identify";
            payload["identifyScenario"] = Input.IdentifyScenarioId!.Trim();
            payload["identifyValidatedFields"] = new[] { "name" };
        }

        return payload;
    }

    private async Task<string?> UploadFileAsync(HttpClient http, IFormFile file)
    {
        using var form = new MultipartFormDataContent();
        await using var stream = file.OpenReadStream();
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType =
            new MediaTypeHeaderValue(file.ContentType.NullIfWhiteSpace() ?? "application/pdf");
        form.Add(fileContent, "file", Path.GetFileName(file.FileName));

        using var response = await http.PostAsync(
            "api/files",
            form,
            HttpContext.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            await SetProviderFailureAsync(response, "upload the PDF");
            return null;
        }

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(HttpContext.RequestAborted));
        return GetRequiredId(document.RootElement, "uploaded file");
    }

    private async Task<JsonDocument?> PostJsonAsync(
        HttpClient http,
        string path,
        object payload,
        string operation)
    {
        using var response = await http.PostAsync(
            path,
            new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"),
            HttpContext.RequestAborted);

        if (!response.IsSuccessStatusCode)
        {
            await SetProviderFailureAsync(response, operation);
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(HttpContext.RequestAborted);
        return string.IsNullOrWhiteSpace(body)
            ? JsonDocument.Parse("{}")
            : JsonDocument.Parse(body);
    }

    private async Task<string?> CreateEmbedUrlWithRetryAsync(HttpClient http)
    {
        var callbackUrl = AddFlowMarker(Input.CallbackUrl.Trim(), FlowId!);
        var payload = new
        {
            returnUrl = callbackUrl,
            failureUrl = callbackUrl,
            expireAt = DateTimeOffset.UtcNow
                .AddMinutes(Input.EmbedLinkExpiration)
                .ToString("O")
        };

        HttpResponseMessage? lastResponse = null;
        const int maximumAttempts = 20;
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            lastResponse?.Dispose();
            lastResponse = await http.PostAsync(
                $"api/envelopes/{EnvelopeId}/recipients/{RecipientId}/embed",
                new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json"),
                HttpContext.RequestAborted);

            if (lastResponse.IsSuccessStatusCode)
            {
                using (lastResponse)
                {
                    using var document = JsonDocument.Parse(
                        await lastResponse.Content.ReadAsStringAsync(HttpContext.RequestAborted));
                    if (document.RootElement.TryGetProperty("url", out var url))
                    {
                        return url.ValueKind == JsonValueKind.String
                            ? url.GetString()
                            : url.TryGetProperty("url", out var nestedUrl)
                                ? nestedUrl.GetString()
                                : null;
                    }

                    ErrorMessage = "DigiSign created the signing session but returned no embed URL.";
                    return null;
                }
            }

            if (attempt < maximumAttempts &&
                (lastResponse.StatusCode is
                    System.Net.HttpStatusCode.BadRequest or
                    System.Net.HttpStatusCode.Conflict or
                    System.Net.HttpStatusCode.UnprocessableEntity))
            {
                await Task.Delay(TimeSpan.FromSeconds(2), HttpContext.RequestAborted);
                continue;
            }

            break;
        }

        if (lastResponse is not null)
        {
            using (lastResponse)
            {
                await SetProviderFailureAsync(
                    lastResponse,
                    "create the embedded signing URL after sending the envelope");
            }
        }

        return null;
    }

    private async Task SetProviderFailureAsync(
        HttpResponseMessage response,
        string operation)
    {
        var body = await response.Content.ReadAsStringAsync(HttpContext.RequestAborted);
        ErrorMessage =
            $"DigiSign could not {operation} ({(int)response.StatusCode} {response.StatusCode}).";
        ProviderError = FormatProviderError(body);
        logger.LogError(
            "DigiSign failed to {Operation}: {Status} {Body}",
            operation,
            response.StatusCode,
            body);
    }

    private async Task LoadIdentifyScenariosAsync()
    {
        HttpClient http;
        try
        {
            http = await authenticationCache.CreateAuthenticatedClientAsync(
                cancellationToken: HttpContext.RequestAborted);
        }
        catch (DigiSignAuthenticationException exception)
        {
            ErrorMessage = exception.Message;
            return;
        }

        using var response = await http.GetAsync(
            "api/account/identify-scenarios/info",
            HttpContext.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            await SetProviderFailureAsync(response, "load Identify scenarios");
            return;
        }

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(HttpContext.RequestAborted));
        foreach (var item in document.RootElement.EnumerateArray())
        {
            IdentifyScenarios.Add(new IdentifyScenarioOption(
                item.GetProperty("id").GetString() ?? "",
                item.GetProperty("name").GetString() ?? "",
                item.TryGetProperty("approvalMode", out var approvalMode)
                    ? approvalMode.GetString() ?? ""
                    : ""));
        }

        if (IdentifyScenarios.Count == 1)
        {
            Input.IdentifyScenarioId = IdentifyScenarios[0].Id;
            Input.SigningMethod = IdentifyMethod;
            ModelState.Remove("Input.IdentifyScenarioId");
            ModelState.Remove("Input.SigningMethod");
        }

        SuccessMessage = $"Loaded {IdentifyScenarios.Count} Identify scenario(s).";
    }

    private void LoadDefaults()
    {
        var cfg = configuration.GetSection("DigiSign");
        Input.SigningMethod = BankIdSignMethod;
        Input.IdentifyScenarioId = cfg["ScenarioId"] ?? "";
        Input.EnvelopeName = "PoC document signing";
        Input.EmailBody = "Please review and sign the attached document.";
        Input.CallbackUrl = cfg["SigningRedirectUrl"].NullIfWhiteSpace()
                            ?? "https://sign.revolving.dev.linksoft.cz/SigningCallback";
        Input.EmbedLinkExpiration = 5;
        Input.SignaturePage = 1;
        Input.SignatureX = 72;
        Input.SignatureY = 650;
    }

    private bool ValidateInput()
    {
        if (!Authentication.IsConfigured)
        {
            ErrorMessage = "Configure shared DigiSign credentials before starting signing.";
            return false;
        }

        if (Input.Document is null || Input.Document.Length == 0)
        {
            ErrorMessage = "Select a non-empty PDF document.";
            return false;
        }

        if (!string.Equals(
                Path.GetExtension(Input.Document.FileName),
                ".pdf",
                StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "This PoC accepts PDF documents only.";
            return false;
        }

        if (Input.Document.Length > 25_000_000)
        {
            ErrorMessage = "The PDF must be 25 MB or smaller.";
            return false;
        }

        if (Input.SigningMethod is not (BankIdSignMethod or IdentifyMethod))
        {
            ErrorMessage = "Select Bank iD SIGN or DigiSign Identify.";
            return false;
        }

        if (Input.SigningMethod == IdentifyMethod &&
            string.IsNullOrWhiteSpace(Input.IdentifyScenarioId))
        {
            ErrorMessage = "Select an Identify scenario for Identify-protected signing.";
            return false;
        }

        if (!new EmailAddressAttribute().IsValid(Input.SignerEmail))
        {
            ErrorMessage = "Enter a valid signer email address.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Input.SignerName) ||
            string.IsNullOrWhiteSpace(Input.EnvelopeName) ||
            string.IsNullOrWhiteSpace(Input.EmailBody))
        {
            ErrorMessage = "Enter the envelope name, email text, and signer name.";
            return false;
        }

        if (!Uri.TryCreate(Input.CallbackUrl, UriKind.Absolute, out var callbackUri) ||
            (callbackUri.Scheme != Uri.UriSchemeHttp &&
             callbackUri.Scheme != Uri.UriSchemeHttps))
        {
            ErrorMessage = "Signing callback URL must be an absolute HTTP or HTTPS URL.";
            return false;
        }

        if (Input.SignaturePage < 1 ||
            Input.SignatureX < 0 ||
            Input.SignatureY < 0)
        {
            ErrorMessage = "Signature page and coordinates must be non-negative, and pages start at 1.";
            return false;
        }

        if (Input.EmbedLinkExpiration is < 1 or > 60)
        {
            ErrorMessage = "Embed link validity must be between 1 and 60 minutes.";
            return false;
        }

        return true;
    }

    private static string GetRequiredId(JsonElement root, string resourceName)
    {
        if (root.TryGetProperty("id", out var id) &&
            id.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(id.GetString()))
        {
            return id.GetString()!;
        }

        throw new DigiSignProviderException(
            $"DigiSign did not return an ID for the {resourceName}.");
    }

    private static string AddFlowMarker(string callbackUrl, string flowId)
    {
        var uri = new UriBuilder(callbackUrl);
        var existingQuery = uri.Query.TrimStart('?');
        uri.Query = string.IsNullOrEmpty(existingQuery)
            ? $"signingFlow={Uri.EscapeDataString(flowId)}"
            : $"{existingQuery}&signingFlow={Uri.EscapeDataString(flowId)}";
        return uri.Uri.AbsoluteUri;
    }

    private static string FormatProviderError(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "DigiSign returned no error details.";
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            return JsonSerializer.Serialize(
                document.RootElement,
                new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return responseBody.Length <= 2000
                ? responseBody
                : $"{responseBody[..2000]}...";
        }
    }

    public sealed class InputModel
    {
        public IFormFile? Document { get; set; }

        [Required]
        public string EnvelopeName { get; set; } = "";

        [Required]
        public string EmailBody { get; set; } = "";

        [Required]
        public string SignerName { get; set; } = "";

        [Required]
        public string SignerEmail { get; set; } = "";

        public string? SignerMobile { get; set; }
        public bool SuppressInvitationEmail { get; set; }

        [Required]
        public string SigningMethod { get; set; } = BankIdSignMethod;

        public string? IdentifyScenarioId { get; set; }

        [Required]
        public string CallbackUrl { get; set; } = "";

        public int EmbedLinkExpiration { get; set; }
        public int SignaturePage { get; set; }
        public int SignatureX { get; set; }
        public int SignatureY { get; set; }
    }

    public sealed record IdentifyScenarioOption(
        string Id,
        string Name,
        string ApprovalMode);

    private sealed class DigiSignProviderException(string message) : Exception(message);
}
