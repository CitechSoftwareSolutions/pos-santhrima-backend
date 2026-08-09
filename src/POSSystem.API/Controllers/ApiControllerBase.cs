using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace POSSystem.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public abstract class ApiControllerBase : ControllerBase
{
    protected Guid CurrentUserId
    {
        get
        {
            var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(idClaim, out var id) ? id : throw new UnauthorizedAccessException("Invalid user context.");
        }
    }
}
