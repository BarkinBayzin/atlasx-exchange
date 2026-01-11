using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace AtlasX.Api.Tests;

internal static class TestAuthToken
{
    internal const string SigningKey = "atlasx-dev-secret-key-please-change";

    internal static string CreateToken(params string[] scopes)
    {
        var claims = new List<Claim>
        {
            new("scope", string.Join(' ', scopes))
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
