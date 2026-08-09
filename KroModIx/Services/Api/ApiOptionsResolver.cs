namespace KroModIx.Services.Api;

/// <summary>Merged <see cref="AppSettings"/> mit <see cref="AppLaunchOptions"/> zu
/// den effektiven API-Optionen. CLI gewinnt über Settings — konsistent mit der
/// Doku in <c>KroModIx.RestApi/README.md</c>.</summary>
internal static class ApiOptionsResolver
{
    public static ApiOptions Resolve(AppSettings settings, AppLaunchOptions cli)
    {
        var port = cli.ApiPortOverride ?? settings.ApiPort;
        var token = cli.ApiTokenOverride ?? settings.ApiBearerToken;
        // CLI-Port setzt Enabled implizit auf true — sonst hätte --api-port
        // ohne settings.ApiEnabled keinen Effekt und der User müsste beides
        // setzen. Bei reinem Settings-Weg zählt ApiEnabled.
        var enabled = cli.ApiPortOverride is not null || settings.ApiEnabled;
        return new ApiOptions(enabled, port, token);
    }
}
