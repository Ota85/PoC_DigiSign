using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigiSignPoC.Pages;

public class IdentifyModel(
    DigiSignAuthenticationCache authenticationCache,
    IConfiguration configuration,
    ILogger<IdentifyModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public DigiSignAuthenticationSnapshot Authentication { get; private set; } =
        authenticationCache.GetSnapshot();
    public DigiSignAuthenticationValidation AuthenticationValidation { get; private set; } =
        DigiSignAuthenticationValidation.NotChecked;
    public bool CanUseWorkflow =>
        Authentication.IsConfigured &&
        !AuthenticationValidation.RequiresReauthentication;

    public string? ErrorMessage { get; private set; }
    public string? ProviderError { get; private set; }
    public string? SuccessMessage { get; private set; }
    public string? IdentificationId { get; private set; }
    public string? VerificationUrl { get; private set; }
    public string? FlowId { get; private set; }
    public string? ValidTo { get; private set; }
    public List<ScenarioOption> Scenarios { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Authentication = authenticationCache.GetSnapshot();
        LoadDefaults();
        AuthenticationValidation = await authenticationCache.ValidateBearerTokenAsync(
            HttpContext.RequestAborted);
        Authentication = authenticationCache.GetSnapshot();
    }

    public IActionResult OnGetVerificationState(string? flowId)
    {
        Response.Headers.CacheControl = "no-store, no-cache";

        var activeFlowId = HttpContext.Session.GetString(DigiSignSession.FlowIdKey);
        if (string.IsNullOrWhiteSpace(flowId) ||
            string.IsNullOrWhiteSpace(activeFlowId) ||
            !string.Equals(flowId, activeFlowId, StringComparison.Ordinal))
        {
            return new JsonResult(new
            {
                completed = false,
                error = "This verification is no longer active."
            })
            {
                StatusCode = StatusCodes.Status409Conflict
            };
        }

        var completion = DigiSignSession.GetCompletion(HttpContext.Session);
        var isCompleted = completion is not null &&
                          string.Equals(completion.FlowId, flowId, StringComparison.Ordinal);

        return new JsonResult(new
        {
            completed = isCompleted,
            receivedAt = isCompleted ? completion!.ReceivedAt : (DateTimeOffset?)null,
            resultUrl = isCompleted ? Url.Page("/Result", new { flowId }) : null
        });
    }

    public async Task OnPostLoadScenariosAsync()
    {
        Authentication = authenticationCache.GetSnapshot();
        AuthenticationValidation = await authenticationCache.ValidateBearerTokenAsync(
            HttpContext.RequestAborted);
        Authentication = authenticationCache.GetSnapshot();
        if (AuthenticationValidation.RequiresReauthentication)
        {
            return;
        }

        await LoadScenariosAsync();
    }

    public async Task OnPostStartAsync()
    {
        Authentication = authenticationCache.GetSnapshot();
        AuthenticationValidation = await authenticationCache.ValidateBearerTokenAsync(
            HttpContext.RequestAborted);
        Authentication = authenticationCache.GetSnapshot();
        if (AuthenticationValidation.RequiresReauthentication)
        {
            return;
        }

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

        var flowId = Guid.NewGuid().ToString("N");
        var createBody = JsonSerializer.Serialize(new
        {
            identifyScenario = Input.ScenarioId,
            redirectUrl = AddFlowMarker(Input.RedirectUrl, flowId),
            name = Input.Name
        });

        HttpResponseMessage createResponse;
        try
        {
            createResponse = await http.PostAsync(
                "api/identifications",
                new StringContent(createBody, Encoding.UTF8, "application/json"),
                HttpContext.RequestAborted);
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "Creating DigiSign identification failed.");
            ErrorMessage = "DigiSign could not be reached while creating the identification.";
            return;
        }

        if (!createResponse.IsSuccessStatusCode)
        {
            var error = await createResponse.Content.ReadAsStringAsync(HttpContext.RequestAborted);
            logger.LogError(
                "POST /api/identifications failed: {Status} {Body}",
                createResponse.StatusCode,
                error);
            ErrorMessage = $"Failed to create identification ({createResponse.StatusCode}).";
            ProviderError = GetProviderError(error);
            return;
        }

        using (var createDocument = JsonDocument.Parse(
                   await createResponse.Content.ReadAsStringAsync(HttpContext.RequestAborted)))
        {
            IdentificationId = createDocument.RootElement.GetProperty("id").GetString();
        }

        if (string.IsNullOrWhiteSpace(IdentificationId))
        {
            ErrorMessage = "DigiSign did not return an identification ID.";
            return;
        }

        object startPayload = Input.LinkExpiration > 0
            ? new { linkExpiration = Input.LinkExpiration }
            : new { };

        HttpResponseMessage startResponse;
        try
        {
            startResponse = await http.PostAsync(
                $"api/identifications/{IdentificationId}/start",
                new StringContent(
                    JsonSerializer.Serialize(startPayload),
                    Encoding.UTF8,
                    "application/json"),
                HttpContext.RequestAborted);
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(
                exception,
                "Starting DigiSign identification {IdentificationId} failed.",
                IdentificationId);
            ErrorMessage = $"Identification {IdentificationId} was created, but DigiSign could not be reached to start it.";
            return;
        }

        if (!startResponse.IsSuccessStatusCode)
        {
            var error = await startResponse.Content.ReadAsStringAsync(HttpContext.RequestAborted);
            ErrorMessage =
                $"Identification {IdentificationId} was created, but it could not be started ({startResponse.StatusCode}).";
            ProviderError = GetProviderError(error);
            return;
        }

        using (var startDocument = JsonDocument.Parse(
                   await startResponse.Content.ReadAsStringAsync(HttpContext.RequestAborted)))
        {
            VerificationUrl = startDocument.RootElement.GetProperty("identifyUrl").GetString();
            ValidTo = startDocument.RootElement.TryGetProperty("validTo", out var validTo)
                ? validTo.GetString()
                : null;
        }

        if (string.IsNullOrWhiteSpace(VerificationUrl))
        {
            ErrorMessage = "DigiSign did not return an Identify URL.";
            return;
        }

        FlowId = flowId;
        HttpContext.Session.SetString(DigiSignSession.BaseUrlKey, Authentication.BaseUrl);
        HttpContext.Session.SetString(DigiSignSession.IdentificationIdKey, IdentificationId);
        HttpContext.Session.SetString(DigiSignSession.FlowIdKey, FlowId);
        DigiSignSession.ClearCompletion(HttpContext.Session);

        logger.LogInformation(
            "Identification {IdentificationId} created and started for flow {FlowId}.",
            IdentificationId,
            FlowId);
    }

    private async Task LoadScenariosAsync()
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

        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(
                "api/account/identify-scenarios/info",
                HttpContext.RequestAborted);
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "Loading DigiSign Identify scenarios failed.");
            ErrorMessage = "DigiSign could not be reached while loading Identify scenarios.";
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            ErrorMessage = $"Failed to load Identify scenarios ({response.StatusCode}).";
            return;
        }

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(HttpContext.RequestAborted));
        foreach (var item in document.RootElement.EnumerateArray())
        {
            Scenarios.Add(new ScenarioOption(
                item.GetProperty("id").GetString() ?? "",
                item.GetProperty("name").GetString() ?? "",
                item.TryGetProperty("approvalMode", out var mode)
                    ? mode.GetString() ?? ""
                    : ""));
        }

        if (Scenarios.Count == 1)
        {
            Input.ScenarioId = Scenarios[0].Id;
            ModelState.Remove("Input.ScenarioId");
            SuccessMessage = $"Loaded and selected scenario: {Scenarios[0].Name}.";
        }
        else
        {
            SuccessMessage = $"Loaded {Scenarios.Count} Identify scenario(s).";
        }
    }

    private void LoadDefaults()
    {
        var cfg = configuration.GetSection("DigiSign");
        Input.ScenarioId = cfg["ScenarioId"] ?? "";
        Input.Name = cfg["Name"] ?? "PoC Verification";
        Input.RedirectUrl = cfg["RedirectUrl"].NullIfWhiteSpace()
                            ?? "https://sign.revolving.dev.linksoft.cz/Callback";
        Input.LinkExpiration = cfg.GetValue<int>("LinkExpiration");
    }

    private bool ValidateInput()
    {
        if (!Authentication.IsConfigured)
        {
            ErrorMessage = "Configure shared DigiSign credentials before starting Identify.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Input.ScenarioId))
        {
            ErrorMessage = "Enter or load a DigiSign Identify scenario.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Input.Name))
        {
            ErrorMessage = "Enter a display name.";
            return false;
        }

        if (!Uri.TryCreate(Input.RedirectUrl, UriKind.Absolute, out var redirectUri) ||
            (redirectUri.Scheme != Uri.UriSchemeHttps &&
             redirectUri.Scheme != Uri.UriSchemeHttp))
        {
            ErrorMessage = "Redirect URL must be an absolute HTTP or HTTPS URL.";
            return false;
        }

        if (Input.LinkExpiration is < 0 or > 10080)
        {
            ErrorMessage = "Link expiration must be 0 or between 1 and 10080 minutes.";
            return false;
        }

        Input.ScenarioId = Input.ScenarioId.Trim();
        Input.Name = Input.Name.Trim();
        Input.RedirectUrl = Input.RedirectUrl.Trim();
        return true;
    }

    private static string AddFlowMarker(string redirectUrl, string flowId)
    {
        var uri = new UriBuilder(redirectUrl);
        var existingQuery = uri.Query.TrimStart('?');
        uri.Query = string.IsNullOrEmpty(existingQuery)
            ? $"pocFlow={Uri.EscapeDataString(flowId)}"
            : $"{existingQuery}&pocFlow={Uri.EscapeDataString(flowId)}";
        return uri.Uri.AbsoluteUri;
    }

    private static string GetProviderError(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "DigiSign returned no error details.";
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            foreach (var propertyName in new[] { "detail", "message", "error", "title" })
            {
                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty(propertyName, out var value) &&
                    value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    return value.GetString()!;
                }
            }

            return root.GetRawText();
        }
        catch (JsonException)
        {
            return responseBody;
        }
    }

    public sealed class InputModel
    {
        public string? ScenarioId { get; set; }

        [Required]
        public string Name { get; set; } = "";

        [Required]
        public string RedirectUrl { get; set; } = "";

        public int LinkExpiration { get; set; }
    }

    public sealed record ScenarioOption(string Id, string Name, string ApprovalMode);
}
