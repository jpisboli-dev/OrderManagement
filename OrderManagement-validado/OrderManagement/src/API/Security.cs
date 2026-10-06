using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using OrderManagement.Application;
using OrderManagement.Domain;
namespace OrderManagement.API;
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid Id => Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new UnauthorizedException();
    public bool IsAdmin => accessor.HttpContext?.User.IsInRole("Admin") == true;
}
public sealed class Passwords : IPasswords
{
    private readonly PasswordHasher<string> hasher = new();
    public string Hash(string password) => hasher.HashPassword("", password);
    public bool Verify(string hash, string password) => hasher.VerifyHashedPassword("", hash, password) != PasswordVerificationResult.Failed;
}
public sealed class Tokens(IConfiguration config) : ITokens
{
    public string Create(Customer customer)
    {
        var token = new JwtSecurityToken(config["Jwt:Issuer"], config["Jwt:Audience"],
            [new Claim(ClaimTypes.NameIdentifier, customer.Id.ToString()), new Claim(ClaimTypes.Role, customer.Role), new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())],
            expires: DateTime.UtcNow.AddHours(1), signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
