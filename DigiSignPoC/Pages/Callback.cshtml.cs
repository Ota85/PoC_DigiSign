using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigiSignPoC.Pages;

public class CallbackModel(IHttpClientFactory httpClientFactory, ILogger<CallbackModel> logger) : PageModel
{
    public string? IdentificationId { get; private set; }
    public string? ProviderStatus { get; private set; }
    public string? ApprovalMode { get; private set; }
    public string? CompletedAt { get; private set; }
    public string? ApprovedAt { get; private set; }
    public string? DeniedAt { get; private set; }
    public int? ProviderHttpStatus { get; private set; }
    public string? ProviderResponseJson { get; private set; }
    public string? ErrorMessage { get; private set; }
    public bool IsPopupCallback { get; private set; }
    public Dictionary<string, string> QueryParams { get; private set; } = [];

    public async Task OnGetAsync()
    {
        IsPopupCallback = Request.Query["pocPopup"] == "1";

        foreach (var (key, value) in Request.Query)
        {
            QueryParams[key] = value.ToString();
        }

        IdentificationId = HttpContext.Session.GetString("DigiSign:IdentificationId");
        var baseUrl = HttpContext.Session.GetString("DigiSign:BaseUrl");
        var bearerToken = HttpContext.Session.GetString("DigiSign:BearerToken");

        if (string.IsNullOrWhiteSpace(IdentificationId) ||
            string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(bearerToken))
        {
            ErrorMessage = "The PoC session does not contain a started identification. Start a new verification from the home page.";
            return;
        }

        var http = httpClientFactory.CreateClient("DigiSign");
        http.BaseAddress = new Uri($"{baseUrl.TrimEnd('/')}/");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync($"api/identifications/{IdentificationId}");
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "Loading DigiSign identification {IdentificationId} failed.", IdentificationId);
            ErrorMessage = "DigiSign API could not be reached while loading the identification result.";
            return;
        }
        var responseBody = await response.Content.ReadAsStringAsync();
        ProviderHttpStatus = (int)response.StatusCode;
        ProviderResponseJson = FormatProviderResponse(responseBody);

        if (!response.IsSuccessStatusCode)
        {
            ErrorMessage = $"DigiSign returned {response.StatusCode} while loading the authoritative identification result.";
            return;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(responseBody);
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "DigiSign returned invalid JSON for identification {IdentificationId}.", IdentificationId);
            ErrorMessage = "DigiSign returned a successful response, but its result was not valid JSON.";
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            ProviderStatus = root.TryGetProperty("status", out var status) ? status.GetString() : null;
            CompletedAt = GetOptionalString(root, "completedAt");
            ApprovedAt = GetOptionalString(root, "approvedAt");
            DeniedAt = GetOptionalString(root, "deniedAt");

            if (root.TryGetProperty("scenarioVersion", out var scenarioVersion) &&
                scenarioVersion.ValueKind == JsonValueKind.Object &&
                scenarioVersion.TryGetProperty("approvalMode", out var approvalMode))
            {
                ApprovalMode = approvalMode.GetString();
            }
        }

        logger.LogInformation(
            "Identification {IdentificationId} callback processed with authoritative status {Status}.",
            IdentificationId,
            ProviderStatus);
    }

    private static string? GetOptionalString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string FormatProviderResponse(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "(empty response body)";
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
            return responseBody;
        }
    }
}
