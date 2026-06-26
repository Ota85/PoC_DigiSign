using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigiSignPoC.Pages;

public class IndexModel(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<IndexModel> logger)
    : PageModel
{
    public string? ErrorMessage { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        var scenarioId = configuration["DigiSign:ScenarioId"]!;
        var callbackUrl = $"{Request.Scheme}://{Request.Host}/Callback";

        logger.LogDebug("Creating identification for scenario {ScenarioId}, callback {CallbackUrl}", scenarioId, callbackUrl);

        var http = httpClientFactory.CreateClient("DigiSign");

        // Step 1: Create an identification
        var createBody = JsonSerializer.Serialize(new { scenarioId });
        var createResponse = await http.PostAsync(
            "api/identifications",
            new StringContent(createBody, Encoding.UTF8, "application/json"));

        if (!createResponse.IsSuccessStatusCode)
        {
            var error = await createResponse.Content.ReadAsStringAsync();
            logger.LogError("POST /api/identifications failed: {Status} {Body}", createResponse.StatusCode, error);
            ErrorMessage = $"Failed to create identification ({createResponse.StatusCode}).";
            return Page();
        }

        var createJson = await createResponse.Content.ReadAsStringAsync();
        logger.LogDebug("POST /api/identifications response: {Body}", createJson);

        using var createDoc = JsonDocument.Parse(createJson);
        var identificationId = createDoc.RootElement.GetProperty("id").GetString()!;

        logger.LogInformation("Identification created: {IdentificationId}", identificationId);

        // Step 2: Start the identification to obtain the verification URL
        var startBody = JsonSerializer.Serialize(new { redirectUrl = callbackUrl });
        var startResponse = await http.PostAsync(
            $"api/identifications/{identificationId}/start",
            new StringContent(startBody, Encoding.UTF8, "application/json"));

        if (!startResponse.IsSuccessStatusCode)
        {
            var error = await startResponse.Content.ReadAsStringAsync();
            logger.LogError("POST /api/identifications/{Id}/start failed: {Status} {Body}", identificationId, startResponse.StatusCode, error);
            ErrorMessage = $"Failed to start identification ({startResponse.StatusCode}).";
            return Page();
        }

        var startJson = await startResponse.Content.ReadAsStringAsync();
        logger.LogDebug("POST /api/identifications/{Id}/start response: {Body}", identificationId, startJson);

        using var startDoc = JsonDocument.Parse(startJson);
        var verificationUrl = startDoc.RootElement.GetProperty("url").GetString()!;

        logger.LogInformation("Verification URL obtained: {Url}", verificationUrl);

        // Redirect the browser to the DigiSign verification page.
        // DigiSign must not run inside an iframe, so we use a top-level redirect.
        return Redirect(verificationUrl);
    }
}
