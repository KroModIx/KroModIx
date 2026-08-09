namespace KroModIx.Services.Api;

/// <summary>Resolved API-Config nach Merge von settings.json + CLI-Args.
/// CLI gewinnt gegen Settings. <see cref="Enabled"/> ist true wenn entweder
/// ein CLI-Port gesetzt wurde ODER settings.ApiEnabled=true (mit gültigem Port).</summary>
public sealed record ApiOptions(bool Enabled, int Port, string? BearerToken);
