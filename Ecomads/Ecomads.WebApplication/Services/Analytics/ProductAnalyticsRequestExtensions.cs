using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Ecomads.WebApplication.Models;

namespace Ecomads.WebApplication.Services.Analytics;

public static class ProductAnalyticsRequestExtensions
{
    public static ProductUsageEventCreateDto WithRequestContext(
        this ProductUsageEventCreateDto dto,
        HttpContext httpContext)
    {
        dto.Path = httpContext.Request.Path.Value;
        dto.Method = httpContext.Request.Method;
        dto.UserAgent = httpContext.Request.Headers.UserAgent.ToString();
        dto.IpHash = HashIpAddress(httpContext.Connection.RemoteIpAddress?.ToString());
        return dto;
    }

    public static Guid? GetCurrentUserId(this ClaimsPrincipal user)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userId, out var parsedUserId) ? parsedUserId : null;
    }

    private static string? HashIpAddress(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return null;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(ipAddress));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
