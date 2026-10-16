using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace Arkya.Admin.Pages;
[IgnoreAntiforgeryToken]
public sealed class ErrorModel : PageModel
{
    public void OnGet() => Response.StatusCode = 500;
    public void OnPost() => Response.StatusCode = 500;
}
