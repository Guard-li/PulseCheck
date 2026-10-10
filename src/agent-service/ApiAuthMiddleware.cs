using System.Net;
using System.Text.Json;
using PulseCheck.Agent.Security;
using PulseCheck.Contracts;

namespace PulseCheck.AgentService;

public sealed class ApiAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly JsonSerializerOptions _json = JsonDefaults.Create();

    public ApiAuthMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AgentSecurity security)
    {
        if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        if (path.Equals("/api/v1/health", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/v1/auth/pair", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/v1/auth/token", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }
        if (path.Equals("/api/v1/pairing/info", StringComparison.OrdinalIgnoreCase) && IsLoopback(context.Connection.RemoteIpAddress))
        {
            await _next(context);
            return;
        }

        var token = ExtractToken(context);
        var session = security.Authenticate(token);
        if (session is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json; charset=utf-8";
            await JsonSerializer.SerializeAsync(context.Response.Body, ApiResponse<object>.Fail("AUTH_REQUIRED", "需要有效的 PulseCheck 访问令牌", "先通过一次性配对码完成配对。"), _json, context.RequestAborted);
            return;
        }
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method) && !string.Equals(session.Role, "owner", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json; charset=utf-8";
            await JsonSerializer.SerializeAsync(context.Response.Body, ApiResponse<object>.Fail("OWNER_REQUIRED", "该操作需要设备所有者权限"), _json, context.RequestAborted);
            return;
        }
        context.Items["PulseCheck.AuthSession"] = session;
        await _next(context);
    }

    private static string? ExtractToken(HttpContext context)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return authorization["Bearer ".Length..].Trim();
        var token = context.Request.Headers["X-PulseCheck-Token"].ToString();
        if (!string.IsNullOrWhiteSpace(token)) return token.Trim();
        if (context.WebSockets.IsWebSocketRequest)
        {
            foreach (var protocol in context.WebSockets.WebSocketRequestedProtocols)
                if (protocol.StartsWith("token.", StringComparison.OrdinalIgnoreCase)) return protocol["token.".Length..].Trim();
            var query = context.Request.Query["access_token"].ToString();
            if (!string.IsNullOrWhiteSpace(query)) return query.Trim();
        }
        return null;
    }

    public static bool IsLoopback(IPAddress? address)
    {
        if (address is null) return false;
        if (IPAddress.IsLoopback(address)) return true;
        var mapped = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        return IPAddress.IsLoopback(mapped);
    }
}