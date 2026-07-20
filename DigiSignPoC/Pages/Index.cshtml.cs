using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigiSignPoC.Pages;

public class IndexModel(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<IndexModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; private set; }
    public string? ProviderError { get; private set; }
    public string? SuccessMessage { get; private set; }
    public string? IdentificationId { get; private set; }
    public string? VerificationUrl { get; private set; }
    public string? FlowId { get; private set; }
    public string? ValidTo { get; private set; }
    public string? TokenExpiresAt { get; private set; }
    public List<ScenarioOption> Scenarios { get; private set; } = [];

    public void OnGet()
    {
        var cfg = configuration.GetSection("DigiSign");
        Input.BaseUrl = cfg["BaseUrl"] ?? "https://api.staging.digisign.org";
        Input.BearerToken = cfg["BearerToken"] ?? "";
        Input.AccessKey = cfg["AccessKey"] ?? "";
        Input.ScenarioId = cfg["ScenarioId"] ?? "";
        Input.Name = cfg["Name"] ?? "PoC Verification";
        Input.RedirectUrl = cfg["RedirectUrl"]?.NullIfEmpty()
                            ?? "https://sign.revolving.dev.linksoft.cz/Callback";
        Input.LinkExpiration = cfg.GetValue<int>("LinkExpiration");
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

    public async Task OnPostGetTokenAsync()
    {
        if (!ValidateBaseUrl() || string.IsNullOrWhiteSpace(Input.AccessKey) || string.IsNullOrWhiteSpace(Input.SecretKey))
        {
            ErrorMessage ??= "Enter both DigiSign accessKey and secretKey.";
            return;
        }

        await ObtainTokenAsync();
        Input.SecretKey = "";
        ModelState.Remove("Input.SecretKey");
    }

    public async Task OnPostLoadScenariosAsync()
    {
        if (!await EnsureTokenAsync())
        {
            return;
        }

        HttpResponseMessage response;
        try
        {
            var http = CreateDigiSignClient(Input.BearerToken);
            response = await http.GetAsync("api/account/identify-scenarios/info");
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "Loading DigiSign Identify scenarios failed.");
            ErrorMessage = "DigiSign API could not be reached while loading scenarios.";
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            ErrorMessage = $"Failed to load Identify scenarios ({response.StatusCode}).";
            return;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var item in document.RootElement.EnumerateArray())
        {
            Scenarios.Add(new ScenarioOption(
                item.GetProperty("id").GetString() ?? "",
                item.GetProperty("name").GetString() ?? "",
                item.TryGetProperty("approvalMode", out var mode) ? mode.GetString() ?? "" : ""));
        }

        if (Scenarios.Count == 1)
        {
            Input.ScenarioId = Scenarios[0].Id;
            ModelState.Remove("Input.ScenarioId");
            SuccessMessage = $"Loaded and selected scenario: {Scenarios[0].Name}.";
        }
        else
        {
            SuccessMessage = $"Loaded {Scenarios.Count} Identify scenario(s). Select one before starting verification.";
        }

        Input.SecretKey = "";
        ModelState.Remove("Input.SecretKey");
    }

    public async Task OnPostStartAsync()
    {
        if (!ValidateVerificationInput() || !await EnsureTokenAsync())
        {
            return;
        }

        var http = CreateDigiSignClient(Input.BearerToken);
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
                new StringContent(createBody, Encoding.UTF8, "application/json"));
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "Creating DigiSign identification failed.");
            ErrorMessage = "DigiSign API could not be reached while creating the identification.";
            return;
        }

        if (!createResponse.IsSuccessStatusCode)
        {
            var error = await createResponse.Content.ReadAsStringAsync();
            logger.LogError("POST /api/identifications failed: {Status} {Body}", createResponse.StatusCode, error);
            ErrorMessage = $"Failed to create identification ({createResponse.StatusCode}).";
            ProviderError = GetProviderError(error);
            return;
        }

        using (var createDocument = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync()))
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
                new StringContent(JsonSerializer.Serialize(startPayload), Encoding.UTF8, "application/json"));
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "Starting DigiSign identification {IdentificationId} failed.", IdentificationId);
            ErrorMessage = $"Identification {IdentificationId} was created, but DigiSign API could not be reached to start it.";
            return;
        }

        if (!startResponse.IsSuccessStatusCode)
        {
            var error = await startResponse.Content.ReadAsStringAsync();
            logger.LogError(
                "POST /api/identifications/{Id}/start failed: {Status} {Body}",
                IdentificationId,
                startResponse.StatusCode,
                error);
            ErrorMessage = $"Identification {IdentificationId} was created, but it could not be started ({startResponse.StatusCode}).";
            ProviderError = GetProviderError(error);
            return;
        }

        using (var startDocument = JsonDocument.Parse(await startResponse.Content.ReadAsStringAsync()))
        {
            VerificationUrl = startDocument.RootElement.GetProperty("identifyUrl").GetString();
            ValidTo = startDocument.RootElement.GetProperty("validTo").GetString();
        }

        if (string.IsNullOrWhiteSpace(VerificationUrl))
        {
            ErrorMessage = "DigiSign did not return an identify URL.";
            return;
        }

        FlowId = flowId;
        HttpContext.Session.SetString(DigiSignSession.BaseUrlKey, Input.BaseUrl);
        HttpContext.Session.SetString(DigiSignSession.BearerTokenKey, Input.BearerToken!);
        HttpContext.Session.SetString(DigiSignSession.IdentificationIdKey, IdentificationId);
        HttpContext.Session.SetString(DigiSignSession.FlowIdKey, FlowId);
        DigiSignSession.ClearCompletion(HttpContext.Session);

        Input.SecretKey = "";
        ModelState.Remove("Input.SecretKey");
        logger.LogInformation("Identification {IdentificationId} created and started.", IdentificationId);
    }

    private bool ValidateBaseUrl()
    {
        if (!Uri.TryCreate(Input.BaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            ErrorMessage = "Base URL must be an absolute HTTP or HTTPS URL.";
            return false;
        }

        Input.BaseUrl = Input.BaseUrl.TrimEnd('/');
        return true;
    }

    private bool ValidateVerificationInput()
    {
        if (!ValidateBaseUrl())
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(Input.ScenarioId))
        {
            ErrorMessage = "Enter or load a DigiSign Identify scenario ID.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Input.Name))
        {
            ErrorMessage = "Enter a display name for the identification.";
            return false;
        }

        Input.ScenarioId = Input.ScenarioId.Trim();
        Input.Name = Input.Name.Trim();

        if (!Uri.TryCreate(Input.RedirectUrl, UriKind.Absolute, out var redirectUri) ||
            (redirectUri.Scheme != Uri.UriSchemeHttps && redirectUri.Scheme != Uri.UriSchemeHttp))
        {
            ErrorMessage = "Redirect URL must be an absolute HTTP or HTTPS URL.";
            return false;
        }

        if (Input.LinkExpiration is < 0 or > 10080)
        {
            ErrorMessage = "Link expiration must be 0 (provider default) or between 1 and 10080 minutes.";
            return false;
        }

        return true;
    }

    private async Task<bool> EnsureTokenAsync()
    {
        if (!string.IsNullOrWhiteSpace(Input.BearerToken))
        {
            return ValidateBaseUrl();
        }

        if (string.IsNullOrWhiteSpace(Input.AccessKey) || string.IsNullOrWhiteSpace(Input.SecretKey))
        {
            ErrorMessage = "Enter a bearer token, or enter accessKey and secretKey so the PoC can obtain one.";
            return false;
        }

        return await ObtainTokenAsync();
    }

    private async Task<bool> ObtainTokenAsync()
    {
        if (!ValidateBaseUrl())
        {
            return false;
        }

        var http = CreateDigiSignClient();
        var body = JsonSerializer.Serialize(new
        {
            accessKey = Input.AccessKey,
            secretKey = Input.SecretKey
        });

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(
                "api/auth-token",
                new StringContent(body, Encoding.UTF8, "application/json"));
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "DigiSign token request failed.");
            ErrorMessage = "DigiSign API could not be reached while obtaining a bearer token.";
            return false;
        }

        if (!response.IsSuccessStatusCode)
        {
            ErrorMessage = $"Failed to obtain bearer token ({response.StatusCode}). Check the API keys and environment.";
            return false;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Input.BearerToken = document.RootElement.GetProperty("token").GetString() ?? "";
        TokenExpiresAt = document.RootElement.TryGetProperty("exp", out var exp)
            ? DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64()).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz")
            : null;

        if (string.IsNullOrWhiteSpace(Input.BearerToken))
        {
            ErrorMessage = "DigiSign authentication response did not contain a bearer token.";
            return false;
        }

        ModelState.Remove("Input.BearerToken");
        SuccessMessage = "Bearer token obtained. It can now be used to load scenarios or start verification.";
        return true;
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

            return LimitProviderError(root.GetRawText());
        }
        catch (JsonException)
        {
            return LimitProviderError(responseBody);
        }
    }

    private static string LimitProviderError(string value) =>
        value.Length <= 1500 ? value : $"{value[..1500]}...";

    private static string AddFlowMarker(string redirectUrl, string flowId)
    {
        var uri = new UriBuilder(redirectUrl);
        var existingQuery = uri.Query.TrimStart('?');
        uri.Query = string.IsNullOrEmpty(existingQuery)
            ? $"pocFlow={Uri.EscapeDataString(flowId)}"
            : $"{existingQuery}&pocFlow={Uri.EscapeDataString(flowId)}";
        return uri.Uri.AbsoluteUri;
    }

    private HttpClient CreateDigiSignClient(string? bearerToken = null)
    {
        var http = httpClientFactory.CreateClient("DigiSign");
        http.BaseAddress = new Uri($"{Input.BaseUrl.TrimEnd('/')}/");
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        return http;
    }

    public sealed class InputModel
    {
        [Required]
        public string BaseUrl { get; set; } = "";

        public string? BearerToken { get; set; }
        public string? AccessKey { get; set; }
        public string? SecretKey { get; set; }
        public string? ScenarioId { get; set; }
        public string Name { get; set; } = "";
        public string RedirectUrl { get; set; } = "";
        public int LinkExpiration { get; set; }
    }

    public sealed record ScenarioOption(string Id, string Name, string ApprovalMode);
}

internal static class StringExtensions
{
    internal static string? NullIfEmpty(this string? value) =>
        string.IsNullOrEmpty(value) ? null : value;
}
