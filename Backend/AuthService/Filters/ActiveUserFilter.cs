using System.Security.Claims;
using AuthService.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Filters;

/// <summary>
/// Rechaza (403) al usuario autenticado si su cuenta fue desactivada, aunque su access token siga vigente.
/// </summary>
public class ActiveUserFilter(AuthServiceContext context) : IAsyncActionFilter
{
    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext executing, ActionExecutionDelegate next)
    {
        var active = Guid.TryParse(executing.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            && await context.Users.AnyAsync(u => u.Id == id && u.IsActive);

        if (!active)
        {
            executing.Result = new ObjectResult(new { message = "Tu cuenta no está activa." }) { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        await next();
    }
}
