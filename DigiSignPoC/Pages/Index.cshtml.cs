using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigiSignPoC.Pages;

public class IndexModel(DigiSignAuthenticationCache authenticationCache) : PageModel
{
    public DigiSignAuthenticationSnapshot Authentication { get; private set; } =
        authenticationCache.GetSnapshot();

    public void OnGet()
    {
        Authentication = authenticationCache.GetSnapshot();
    }
}
