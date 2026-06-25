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

        logger.LogInformation("DigiSign callback received. Params: {Params}",
            string.Join(", ", QueryParams.Select(p => $"{p.Key}={p.Value}")));
    }
}

internal static class StringExtensions
{
    internal static string? NullIfEmpty(this string value) =>
        string.IsNullOrEmpty(value) ? null : value;
}
