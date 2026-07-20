using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigiSignPoC.Pages;

public class ResultModel : PageModel
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
    public Dictionary<string, string> QueryParams { get; private set; } = [];

    public void OnGet(string? flowId)
    {
        Response.Headers.CacheControl = "no-store, no-cache";

        var activeFlowId = HttpContext.Session.GetString(DigiSignSession.FlowIdKey);
        var completion = DigiSignSession.GetCompletion(HttpContext.Session);

        if (string.IsNullOrWhiteSpace(flowId) ||
            string.IsNullOrWhiteSpace(activeFlowId) ||
            !string.Equals(flowId, activeFlowId, StringComparison.Ordinal) ||
            completion is null ||
            !string.Equals(completion.FlowId, flowId, StringComparison.Ordinal))
        {
            ErrorMessage = "No completed result was found for this verification. Return to the PoC and start a new verification.";
            return;
        }

        IdentificationId = completion.IdentificationId;
        ProviderStatus = completion.ProviderStatus;
        ApprovalMode = completion.ApprovalMode;
        CompletedAt = completion.CompletedAt;
        ApprovedAt = completion.ApprovedAt;
        DeniedAt = completion.DeniedAt;
        ProviderHttpStatus = completion.ProviderHttpStatus;
        ProviderResponseJson = completion.ProviderResponseJson;
        ErrorMessage = completion.ErrorMessage;
        QueryParams = completion.QueryParams;
    }
}
