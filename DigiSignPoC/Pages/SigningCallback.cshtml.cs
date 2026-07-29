using System.Text.Json;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigiSignPoC.Pages;

public class SigningCallbackModel(
    DigiSignAuthenticationCache authenticationCache,
    ILogger<SigningCallbackModel> logger) : PageModel
{
    public string? FlowId { get; private set; }
    public string? ErrorMessage { get; private set; }
    public bool CompletionRecorded { get; private set; }

    public async Task OnGetAsync(string? signingFlow)
    {
        FlowId = signingFlow;
        var flow = DigiSignSigningSession.GetFlow(HttpContext.Session);
        if (string.IsNullOrWhiteSpace(FlowId) ||
            flow is null ||
            !string.Equals(flow.FlowId, FlowId, StringComparison.Ordinal))
        {
            ErrorMessage =
                "The signing callback does not match the active flow. Return to the PoC and start a new signing flow.";
            return;
        }

        var queryParams = Request.Query.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToString());

        var completion = await LoadCompletionAsync(flow, queryParams);
        DigiSignSigningSession.SetCompletion(HttpContext.Session, completion);
        CompletionRecorded = true;

        logger.LogInformation(
            "Signing callback stored for envelope {EnvelopeId}, flow {FlowId}, status {Status}.",
            flow.EnvelopeId,
            flow.FlowId,
            completion.ProviderStatus);
    }

    private async Task<DigiSignSigningCompletion> LoadCompletionAsync(
        DigiSignSigningFlow flow,
        Dictionary<string, string> queryParams)
    {
        try
        {
            var http = await authenticationCache.CreateAuthenticatedClientAsync(
                flow.BaseUrl,
                HttpContext.RequestAborted);
            using var response = await http.GetAsync(
                $"api/envelopes/{flow.EnvelopeId}",
                HttpContext.RequestAborted);
            var responseBody = await response.Content.ReadAsStringAsync(HttpContext.RequestAborted);
            var formattedResponse = FormatProviderResponse(responseBody);

            if (!response.IsSuccessStatusCode)
            {
                return CreateCompletion(
                    flow,
                    queryParams,
                    providerHttpStatus: (int)response.StatusCode,
                    providerResponseJson: formattedResponse,
                    errorMessage:
                    $"DigiSign returned {response.StatusCode} while loading the envelope result.");
            }

            using var document = JsonDocument.Parse(responseBody);
            var status = document.RootElement.TryGetProperty("status", out var statusElement)
                ? statusElement.GetString()
                : null;

            return CreateCompletion(
                flow,
                queryParams,
                providerStatus: status,
                providerHttpStatus: (int)response.StatusCode,
                providerResponseJson: formattedResponse);
        }
        catch (DigiSignAuthenticationException exception)
        {
            return CreateCompletion(
                flow,
                queryParams,
                errorMessage: exception.Message);
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(
                exception,
                "Loading envelope {EnvelopeId} after callback failed.",
                flow.EnvelopeId);
            return CreateCompletion(
                flow,
                queryParams,
                errorMessage: "DigiSign could not be reached while loading the envelope result.");
        }
        catch (JsonException exception)
        {
            logger.LogError(
                exception,
                "DigiSign returned invalid envelope JSON for {EnvelopeId}.",
                flow.EnvelopeId);
            return CreateCompletion(
                flow,
                queryParams,
                errorMessage: "DigiSign returned an invalid envelope response.");
        }
    }

    private static DigiSignSigningCompletion CreateCompletion(
        DigiSignSigningFlow flow,
        Dictionary<string, string> queryParams,
        string? providerStatus = null,
        int? providerHttpStatus = null,
        string? providerResponseJson = null,
        string? errorMessage = null) =>
        new(
            flow.FlowId,
            flow.EnvelopeId,
            DateTimeOffset.UtcNow,
            providerStatus,
            providerHttpStatus,
            providerResponseJson,
            queryParams.GetValueOrDefault("event"),
            queryParams.GetValueOrDefault("error"),
            errorMessage,
            queryParams);

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
