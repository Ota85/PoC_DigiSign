using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigiSignPoC.Pages;

public class IndexModel(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<IndexModel> logger)
    : PageModel
{
    public string? ErrorMessage { get; private set; }
    public string? IdentificationId { get; private set; }
    public string? VerificationUrl { get; private set; }

    public void OnGet() { }

    public async Task OnPostAsync()
    {
        var cfg = configuration.GetSection("DigiSign");
        var scenarioId = cfg["ScenarioId"]!;
        var name = cfg["Name"] ?? "PoC Verification";
        var redirectUrl = cfg["RedirectUrl"]?.NullIfEmpty()
                          ?? $"{Request.Scheme}://{Request.Host}/Callback";
        var validityMinutes = cfg.GetValue<int>("ValidityMinutes");

        logger.LogDebug(
            "Creating identification: scenario={ScenarioId}, name={Name}, redirectUrl={RedirectUrl}, validityMinutes={ValidityMinutes}",
            scenarioId, name, redirectUrl.Replace('\r', ' ').Replace('\n', ' '), validityMinutes > 0 ? validityMinutes : null);

        var http = httpClientFactory.CreateClient("DigiSign");

        // Step 1: Create an identification (AK-01, AK-02)
        var createBody = JsonSerializer.Serialize(new { scenarioId, name });
        var createResponse = await http.PostAsync(
            "api/identifications",
            new StringContent(createBody, Encoding.UTF8, "application/json"));

        if (!createResponse.IsSuccessStatusCode)
        {
            var error = await createResponse.Content.ReadAsStringAsync();
            logger.LogError("POST /api/identifications failed: {Status} {Body}", createResponse.StatusCode, error);
            ErrorMessage = $"Failed to create identification ({createResponse.StatusCode}).";
            return;
        }

        var createJson = await createResponse.Content.ReadAsStringAsync();
        logger.LogDebug("POST /api/identifications response: {Body}", createJson);

        using var createDoc = JsonDocument.Parse(createJson);
        IdentificationId = createDoc.RootElement.GetProperty("id").GetString()!;

        logger.LogInformation("Identification created: {IdentificationId}", IdentificationId);

        // Step 2: Start the identification to obtain the verification URL (AK-03, AK-06)
        object startPayload = validityMinutes > 0
            ? new { redirectUrl, validityMinutes }
            : new { redirectUrl };

        var startBody = JsonSerializer.Serialize(startPayload);
        var startResponse = await http.PostAsync(
            $"api/identifications/{IdentificationId}/start",
            new StringContent(startBody, Encoding.UTF8, "application/json"));

        if (!startResponse.IsSuccessStatusCode)
        {
            var error = await startResponse.Content.ReadAsStringAsync();
            logger.LogError("POST /api/identifications/{Id}/start failed: {Status} {Body}", IdentificationId, startResponse.StatusCode, error);
            ErrorMessage = $"Failed to start identification ({startResponse.StatusCode}).";
            return;
        }

        var startJson = await startResponse.Content.ReadAsStringAsync();
        logger.LogDebug("POST /api/identifications/{Id}/start response: {Body}", IdentificationId, startJson);

        using var startDoc = JsonDocument.Parse(startJson);
        VerificationUrl = startDoc.RootElement.GetProperty("url").GetString()!;

        logger.LogInformation("Verification URL obtained: {Url}", VerificationUrl);
        // The URL is rendered in the view, which opens it in a new tab (AK-04).
    }
}

internal static class StringExtensions
{
    internal static string? NullIfEmpty(this string? value) =>
        string.IsNullOrEmpty(value) ? null : value;
}
