using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigiSignPoC.Pages;

public class CredentialsModel(
    DigiSignAuthenticationCache authenticationCache,
    ILogger<CredentialsModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public DigiSignAuthenticationSnapshot Authentication { get; private set; } =
        authenticationCache.GetSnapshot();
    public DigiSignAuthenticationValidation AuthenticationValidation { get; private set; } =
        DigiSignAuthenticationValidation.NotChecked;

    public string? ErrorMessage { get; private set; }
    public string? WarningMessage { get; private set; }
    public string? SuccessMessage { get; private set; }

    public async Task OnGetAsync()
    {
        Authentication = authenticationCache.GetSnapshot();
        Input.BaseUrl = Authentication.BaseUrl;
        Input.BearerToken = Authentication.BearerToken;
        AuthenticationValidation = await authenticationCache.ValidateBearerTokenAsync(
            HttpContext.RequestAborted);
        if (AuthenticationValidation.RequiresReauthentication)
        {
            WarningMessage = "Please update the bearer token.";
        }
    }

    public async Task OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            ErrorMessage = "Check the DigiSign environment and credentials.";
            Authentication = authenticationCache.GetSnapshot();
            return;
        }

        try
        {
            Authentication = await authenticationCache.ConfigureAsync(
                Input.BaseUrl,
                Input.BearerToken,
                Input.AccessKey,
                Input.SecretKey,
                HttpContext.RequestAborted);
            AuthenticationValidation = await authenticationCache.ValidateBearerTokenAsync(
                HttpContext.RequestAborted);
            if (AuthenticationValidation.RequiresReauthentication)
            {
                WarningMessage = "Please update the bearer token. DigiSign did not accept the saved token.";
            }
            else if (!AuthenticationValidation.IsValid)
            {
                WarningMessage = AuthenticationValidation.Message;
            }
            else
            {
                SuccessMessage = "DigiSign credentials are configured, validated, and the bearer token is saved for future application starts.";
            }

            Input = new InputModel
            {
                BaseUrl = Authentication.BaseUrl,
                BearerToken = Authentication.BearerToken
            };
            ModelState.Clear();
        }
        catch (DigiSignAuthenticationException exception)
        {
            logger.LogWarning(exception, "Configuring shared DigiSign authentication failed.");
            ErrorMessage = exception.Message;
            Authentication = authenticationCache.GetSnapshot();
            AuthenticationValidation = DigiSignAuthenticationValidation.NotChecked;
        }
    }

    public sealed class InputModel
    {
        [Required]
        public string BaseUrl { get; set; } = "";

        public string? BearerToken { get; set; }
        public string? AccessKey { get; set; }
        public string? SecretKey { get; set; }
    }
}
