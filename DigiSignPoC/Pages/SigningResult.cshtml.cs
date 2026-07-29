using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;

namespace DigiSignPoC.Pages;

public class SigningResultModel(
    DigiSignAuthenticationCache authenticationCache,
    ILogger<SigningResultModel> logger) : PageModel
{
    public DigiSignSigningFlow? Flow { get; private set; }
    public DigiSignSigningCompletion? Completion { get; private set; }
    public string? ErrorMessage { get; private set; }

    public void OnGet(string? flowId)
    {
        Response.Headers.CacheControl = "no-store, no-cache";
        LoadState(flowId);
    }

    public async Task<IActionResult> OnGetRefreshAsync(string? flowId)
    {
        Response.Headers.CacheControl = "no-store, no-cache";
        if (!LoadState(flowId) || Flow is null || Completion is null)
        {
            return Page();
        }

        try
        {
            var http = await authenticationCache.CreateAuthenticatedClientAsync(
                Flow.BaseUrl,
                HttpContext.RequestAborted);
            using var response = await http.GetAsync(
                $"api/envelopes/{Flow.EnvelopeId}",
                HttpContext.RequestAborted);
            var responseBody = await response.Content.ReadAsStringAsync(HttpContext.RequestAborted);

            if (!response.IsSuccessStatusCode)
            {
                ErrorMessage =
                    $"DigiSign returned {response.StatusCode} while refreshing the envelope.";
                return Page();
            }

            using var document = JsonDocument.Parse(responseBody);
            var status = document.RootElement.TryGetProperty("status", out var statusElement)
                ? statusElement.GetString()
                : null;
            var formattedResponse = JsonSerializer.Serialize(
                document.RootElement,
                new JsonSerializerOptions { WriteIndented = true });

            Completion = Completion with
            {
                ReceivedAt = DateTimeOffset.UtcNow,
                ProviderStatus = status,
                ProviderHttpStatus = (int)response.StatusCode,
                ProviderResponseJson = formattedResponse,
                ErrorMessage = null
            };
            DigiSignSigningSession.SetCompletion(HttpContext.Session, Completion);
            return RedirectToPage("/SigningResult", new { flowId });
        }
        catch (DigiSignAuthenticationException exception)
        {
            ErrorMessage = exception.Message;
            return Page();
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(
                exception,
                "Refreshing signed envelope {EnvelopeId} failed.",
                Flow.EnvelopeId);
            ErrorMessage = "DigiSign could not be reached while refreshing the envelope.";
            return Page();
        }
        catch (JsonException exception)
        {
            logger.LogError(
                exception,
                "DigiSign returned invalid JSON while refreshing envelope {EnvelopeId}.",
                Flow.EnvelopeId);
            ErrorMessage = "DigiSign returned an invalid envelope response.";
            return Page();
        }
    }

    public async Task<IActionResult> OnGetDownloadAsync(string? flowId)
    {
        Response.Headers.CacheControl = "no-store, no-cache";
        if (!LoadState(flowId) ||
            Flow is null ||
            Completion is null ||
            ErrorMessage is not null)
        {
            return Page();
        }

        if (!string.Equals(
                Completion.ProviderStatus,
                "completed",
                StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "The signed document can be downloaded only after the envelope is completed.";
            return Page();
        }

        try
        {
            var http = await authenticationCache.CreateAuthenticatedClientAsync(
                Flow.BaseUrl,
                HttpContext.RequestAborted);
            using var response = await http.GetAsync(
                $"api/envelopes/{Flow.EnvelopeId}/download?output=combined&include=signable_documents,audit_log",
                HttpContext.RequestAborted);

            if (!response.IsSuccessStatusCode)
            {
                ErrorMessage =
                    $"DigiSign could not download the signed documents ({response.StatusCode}).";
                return Page();
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(HttpContext.RequestAborted);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/pdf";
            var baseName = Path.GetFileNameWithoutExtension(Flow.OriginalFileName);
            var extension = contentType.Contains("zip", StringComparison.OrdinalIgnoreCase)
                ? ".zip"
                : ".pdf";
            return File(bytes, contentType, $"{baseName}-signed{extension}");
        }
        catch (DigiSignAuthenticationException exception)
        {
            ErrorMessage = exception.Message;
            return Page();
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(
                exception,
                "Downloading signed envelope {EnvelopeId} failed.",
                Flow.EnvelopeId);
            ErrorMessage = "DigiSign could not be reached while downloading the signed document.";
            return Page();
        }
    }

    private bool LoadState(string? flowId)
    {
        var flow = DigiSignSigningSession.GetFlow(HttpContext.Session);
        var completion = DigiSignSigningSession.GetCompletion(HttpContext.Session);

        if (string.IsNullOrWhiteSpace(flowId) ||
            flow is null ||
            completion is null ||
            !string.Equals(flow.FlowId, flowId, StringComparison.Ordinal) ||
            !string.Equals(completion.FlowId, flowId, StringComparison.Ordinal))
        {
            ErrorMessage =
                "No completed signing callback was found. Return to the signing page and start a new flow.";
            return false;
        }

        Flow = flow;
        Completion = completion;
        ErrorMessage = completion.ErrorMessage;
        return true;
    }
}
