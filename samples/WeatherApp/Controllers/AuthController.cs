using System.Security.Claims;
using CosmoApiServer.Core.Auth;
using CosmoApiServer.Core.Controllers;
using CosmoApiServer.Core.Controllers.Attributes;

namespace WeatherApp.Controllers;

public record LoginRequest(string Username, string Password);

[Route("auth")]
public class AuthController(JwtService jwtService) : ControllerBase
{
    // Hardcoded users for demonstration — replace with real user store in production
    private static readonly Dictionary<string, (string Password, string Role)> Users = new()
    {
        ["admin"]  = ("admin123",  "Admin"),
        ["viewer"] = ("viewer123", "Viewer")
    };

    /// POST /auth/login — returns a JWT on success
    [HttpPost("login")]
    [AllowAnonymous]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Username))
            return BadRequest("Username and password are required.");

        if (!Users.TryGetValue(request.Username, out var user) || user.Password != request.Password)
        {
            Response.StatusCode = 401;
            return StatusCode(401, new { error = "Invalid username or password." });
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, request.Username),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("sub", request.Username)
        };

        var token = jwtService.GenerateToken(claims);

        return Ok(new
        {
            token,
            tokenType = "Bearer",
            expiresIn = 3600,
            username = request.Username,
            role = user.Role
        });
    }

    /// GET /auth/me — returns the current authenticated user's claims
    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var name = HttpContext.User!.FindFirst(ClaimTypes.Name)?.Value;
        var role = HttpContext.User!.FindFirst(ClaimTypes.Role)?.Value;
        return Ok(new { username = name, role });
    }
}
