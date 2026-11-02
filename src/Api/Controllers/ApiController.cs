using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PharmaTrack.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public abstract class ApiController : ControllerBase;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Executive = "Executive";
    public const string Dealer = "Dealer";
    public const string CentralStaff = "Admin,Warehouse";
    public const string CentralAll = "Admin,Warehouse,Executive";
    public const string Managers = "Admin,Executive";
}
