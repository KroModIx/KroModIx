using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace KroModIx.Services.Api;

/// <summary>
/// Simpelste denkbare Bearer-Auth: konstanter Vergleich gegen ein statisches
/// Token aus <see cref="AppSettings.ApiBearerToken"/>. Ohne Token → alle
/// Requests <c>403</c>. Kein JWT, keine Expiry — Rotation = Token ändern +
/// App neu starten.
///
/// Middleware ist bewusst KEINE ASP.NET-Auth-Handler (spart Registrierung,
/// Schemes, Policy-Machinery). Für interne Localhost-API ist das overkill.
/// </summary>
internal static class ApiBearerAuth
{
    public static Task Enforce(HttpContext ctx, string? expectedToken, Func<Task> next)
    {
        if (string.IsNullOrWhiteSpace(expectedToken))
            return WriteProblem(ctx, StatusCodes.Status403Forbidden,
                "API disabled", "Kein Bearer-Token in settings.json.ApiBearerToken gesetzt — API-Zugriff verweigert.");

        if (!ctx.Request.Headers.TryGetValue("Authorization", out var authHeader))
            return WriteProblem(ctx, StatusCodes.Status401Unauthorized,
                "Missing Authorization header", "Erwartet: 'Authorization: Bearer <token>'.");

        var header = authHeader.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.Ordinal))
            return WriteProblem(ctx, StatusCodes.Status401Unauthorized,
                "Invalid Authorization header", "Erwartet: 'Authorization: Bearer <token>'.");

        var provided = header[prefix.Length..].Trim();
        if (!FixedTimeEquals(provided, expectedToken))
            return WriteProblem(ctx, StatusCodes.Status403Forbidden,
                "Invalid token", "Bearer-Token stimmt nicht.");

        return next();
    }

    /// <summary>Konstant-Zeit-Vergleich, damit Timing-Attacks nicht das Token
    /// per Zeichen ableiten können. Bei internem Localhost-Traffic praktisch
    /// egal, aber der Aufwand ist minimal.</summary>
    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }

    private static Task WriteProblem(HttpContext ctx, int status, string title, string detail)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/problem+json";
        var problem = new
        {
            type = "about:blank",
            title,
            status,
            detail,
            instance = ctx.Request.Path.Value,
        };
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(problem));
    }
}
