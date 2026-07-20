using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigiSignPoC.Pages;

public class CallbackModel(IHttpClientFactory httpClientFactory, ILogger<CallbackModel> logger) : PageModel
{
    public string? FlowId { get; private set; }
    public string? ErrorMessage { get; private set; }
    public bool CompletionRecorded { get; private set; }

    public async Task OnGetAsync(string? pocFlow)
    {
        FlowId = pocFlow;
        var session = HttpContext.Session;
        var activeFlowId = session.GetString(DigiSignSession.FlowIdKey);

        if (string.IsNullOrWhiteSpace(FlowId) ||
            string.IsNullOrWhiteSpace(activeFlowId) ||
            !string.Equals(FlowId, activeFlowId, StringComparison.Ordinal))
        {
            ErrorMessage = "The callback does not match the active verification. Return to the PoC and start a new verification.";
            return;
        }

        var identificationId = session.GetString(DigiSignSession.IdentificationIdKey);
        var baseUrl = session.GetString(DigiSignSession.BaseUrlKey);
        var bearerToken = session.GetString(DigiSignSession.BearerTokenKey);
        var queryParams = Request.Query.ToDictionary(pair => pair.Key, pair => pair.Value.ToString());

        if (string.IsNullOrWhiteSpace(identificationId) ||
            string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(bearerToken))
        {
            ErrorMessage = "The PoC session does not contain the active DigiSign verification.";
            return;
        }

        var completion = await LoadCompletionAsync(
            FlowId,
            identificationId,
            baseUrl,
            bearerToken,
            queryParams);

        DigiSignSession.SetCompletion(session, completion);
        CompletionRecorded = true;

        logger.LogInformation(
            "Identification {IdentificationId} callback stored for flow {FlowId} with status {Status}.",
            identificationId,
            FlowId,
            completion.ProviderStatus);
    }

    private async Task<DigiSignCompletion> LoadCompletionAsync(
        string flowId,
        string identificationId,
        string baseUrl,
        string bearerToken,
        Dictionary<string, string> queryParams)
    {
        var http = httpClientFactory.CreateClient("DigiSign");
        http.BaseAddress = new Uri($"{baseUrl.TrimEnd('/')}/");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        try
        {
            using var response = await http.GetAsync($"api/identifications/{identificationId}");
            var responseBody = await response.Content.ReadAsStringAsync();
            var formattedResponse = FormatProviderResponse(responseBody);

            if (!response.IsSuccessStatusCode)
            {
                return CreateCompletion(
                    flowId,
                    identificationId,
                    queryParams,
                    providerHttpStatus: (int)response.StatusCode,
                    providerResponseJson: formattedResponse,
                    errorMessage: $"DigiSign returned {response.StatusCode} while loading the authoritative identification result.");
            }

            try
            {
                using var document = JsonDocument.Parse(responseBody);
                var root = document.RootElement;
                var approvalMode = root.TryGetProperty("scenarioVersion", out var scenarioVersion) &&
                                   scenarioVersion.ValueKind == JsonValueKind.Object &&
                                   scenarioVersion.TryGetProperty("approvalMode", out var approvalModeElement)
                    ? approvalModeElement.GetString()
                    : null;

                return CreateCompletion(
                    flowId,
                    identificationId,
                    queryParams,
                    providerStatus: GetOptionalString(root, "status"),
                    approvalMode: approvalMode,
                    completedAt: GetOptionalString(root, "completedAt"),
                    approvedAt: GetOptionalString(root, "approvedAt"),
                    deniedAt: GetOptionalString(root, "deniedAt"),
                    providerHttpStatus: (int)response.StatusCode,
                    providerResponseJson: formattedResponse);
            }
            catch (JsonException exception)
            {
                logger.LogError(exception, "DigiSign returned invalid JSON for identification {IdentificationId}.", identificationId);
                return CreateCompletion(
                    flowId,
                    identificationId,
                    queryParams,
                    providerHttpStatus: (int)response.StatusCode,
                    providerResponseJson: formattedResponse,
                    errorMessage: "DigiSign returned a successful response, but its result was not valid JSON.");
            }
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "Loading DigiSign identification {IdentificationId} failed.", identificationId);
            return CreateCompletion(
                flowId,
                identificationId,
                queryParams,
                errorMessage: "DigiSign API could not be reached while loading the identification result.");
        }
    }

    private static DigiSignCompletion CreateCompletion(
        string flowId,
        string identificationId,
        Dictionary<string, string> queryParams,
        string? providerStatus = null,
        string? approvalMode = null,
        string? completedAt = null,
        string? approvedAt = null,
        string? deniedAt = null,
        int? providerHttpStatus = null,
        string? providerResponseJson = null,
        string? errorMessage = null) =>
        new(
            flowId,
            identificationId,
            DateTimeOffset.UtcNow,
            providerStatus,
            approvalMode,
            completedAt,
            approvedAt,
            deniedAt,
            providerHttpStatus,
            providerResponseJson,
            errorMessage,
            queryParams);

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
