using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigiSignPoC.Pages;

public class CallbackModel(ILogger<CallbackModel> logger) : PageModel
{
    public string? Status { get; private set; }
    public Dictionary<string, string> QueryParams { get; private set; } = [];

    public void OnGet()
    {
        foreach (var (key, value) in Request.Query)
        {
            QueryParams[key] = value.ToString();
        }

        Status = Request.Query["status"].ToString().NullIfEmpty()
               ?? Request.Query["Status"].ToString().NullIfEmpty();

        logger.LogInformation("DigiSign callback received with {ParameterCount} query parameters.", QueryParams.Count);
    }
}
