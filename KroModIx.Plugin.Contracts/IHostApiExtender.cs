using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace KroModIx.Plugin.Contracts;

/// <summary>
/// Optional-Erweiterung für <see cref="IGameModPlugin"/>: registriert HTTP-Endpoints
/// unter <c>/plugins/{plugin-id}/…</c> in der Host-REST-API.
///
/// <para>Wenn ein Plugin dieses Interface implementiert (parallel zu
/// <see cref="IGameModPlugin"/>), fragt der Host nach der Aktivierung
/// <see cref="GetApiEndpoints"/> ab und mountet die Routen. Dieselbe
/// Bearer-Auth wie für die Host-Endpoints gilt automatisch — Plugins
/// kümmern sich nicht um Auth.</para>
///
/// <para>Bewusst framework-frei (kein <c>Microsoft.AspNetCore.*</c>-Verweis):
/// die Contracts sollen für jedes Plugin schlank bleiben, damit niemand
/// nur wegen einer <c>GET /mods</c>-Route ASP.NET Core laden muss. Der Host
/// übersetzt intern <c>HttpContext</c> ↔ <see cref="PluginApiRequest"/>/<see cref="PluginApiResponse"/>.</para>
/// </summary>
public interface IHostApiExtender
{
    /// <summary>Wird nach <see cref="IGameModPlugin.InitializeAsync"/> vom Host aufgerufen.
    /// Kann leer sein (dann wirkt das Interface wie nicht implementiert).</summary>
    IReadOnlyList<PluginApiEndpoint> GetApiEndpoints();
}

/// <summary>Eine vom Plugin bereitgestellte HTTP-Route unter <c>/plugins/{plugin-id}/{RelativePath}</c>.</summary>
/// <param name="HttpMethod">HTTP-Methode (Groß-/Kleinschreibung egal), z.B. <c>"GET"</c>, <c>"POST"</c>.</param>
/// <param name="RelativePath">Pfad relativ zum Plugin-Namespace, z.B. <c>"mods"</c> oder <c>"mods/{id}"</c>.
///   Route-Parameter in geschweiften Klammern werden als <see cref="PluginApiRequest.RouteValues"/> geliefert.</param>
/// <param name="Summary">Kurzbeschreibung für den Discovery-Endpoint <c>GET /plugins</c> (optional).</param>
/// <param name="Handler">Handler-Delegate. Läuft auf einem Kestrel-Thread, NICHT auf dem UI-Thread —
///   Plugins müssen ggf. selbst via <c>Avalonia.Threading.Dispatcher.UIThread.InvokeAsync</c> marshalen.</param>
public sealed record PluginApiEndpoint(
    string HttpMethod,
    string RelativePath,
    string? Summary,
    PluginApiHandler Handler);

/// <summary>Handler-Signatur für <see cref="PluginApiEndpoint.Handler"/>.</summary>
public delegate Task<PluginApiResponse> PluginApiHandler(PluginApiRequest request, CancellationToken cancellationToken);

/// <summary>Eingehende HTTP-Anfrage in framework-freier Form. Vom Host aus einem
/// <c>Microsoft.AspNetCore.Http.HttpContext</c> zusammengebaut.</summary>
public sealed class PluginApiRequest
{
    public string Method { get; }
    public string Path { get; }
    public IReadOnlyDictionary<string, string?> RouteValues { get; }
    public IReadOnlyDictionary<string, string> Query { get; }
    public IReadOnlyDictionary<string, string> Headers { get; }
    /// <summary>Body-Stream. Kann leer sein. Der Host schließt den Stream nach
    /// dem Handler — Plugins müssen ihn nicht disposen.</summary>
    public Stream Body { get; }

    public PluginApiRequest(
        string method,
        string path,
        IReadOnlyDictionary<string, string?> routeValues,
        IReadOnlyDictionary<string, string> query,
        IReadOnlyDictionary<string, string> headers,
        Stream body)
    {
        Method = method;
        Path = path;
        RouteValues = routeValues;
        Query = query;
        Headers = headers;
        Body = body;
    }
}

/// <summary>Antwort vom Plugin-Handler. Statische Factories decken die üblichen
/// Fälle (JSON, Plain-Text, No-Content, Problem-Details). Für Sonderfälle
/// (Binary, Custom-Content-Type) direkt <see cref="PluginApiResponse(int, string?, byte[]?)"/> aufrufen.</summary>
public sealed class PluginApiResponse
{
    public int StatusCode { get; }
    public string? ContentType { get; }
    public byte[]? Body { get; }

    public PluginApiResponse(int statusCode, string? contentType, byte[]? body)
    {
        StatusCode = statusCode;
        ContentType = contentType;
        Body = body;
    }

    /// <summary><c>200 OK</c> mit JSON-Body (Content-Type <c>application/json</c>).
    /// JSON-Serialisierung übernimmt der Host mit CamelCase + Ignore-Null-Werte,
    /// konsistent zu den restlichen API-Responses.</summary>
    public static PluginApiResponse Json(object? value, int statusCode = 200)
        => new(statusCode, "application/json; charset=utf-8",
            System.Text.Encoding.UTF8.GetBytes(
                System.Text.Json.JsonSerializer.Serialize(value, DefaultJson)));

    public static PluginApiResponse Text(string text, int statusCode = 200, string contentType = "text/plain; charset=utf-8")
        => new(statusCode, contentType, System.Text.Encoding.UTF8.GetBytes(text));

    public static PluginApiResponse NoContent() => new(204, null, null);

    public static PluginApiResponse NotFound(string detail)
        => Problem(404, "Not Found", detail);

    public static PluginApiResponse Problem(int statusCode, string title, string detail)
        => new(statusCode, "application/problem+json",
            System.Text.Encoding.UTF8.GetBytes(
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    type = "about:blank",
                    title,
                    status = statusCode,
                    detail,
                }, DefaultJson)));

    private static readonly System.Text.Json.JsonSerializerOptions DefaultJson = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
    };
}
