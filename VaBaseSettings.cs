namespace LoginWithVaBase;

/// <summary>
/// The "VaBase" section of configuration. Everything except the client secret is in appsettings.json.
/// The secret comes from .NET user secrets while developing, or from an environment variable or your
/// host's secret store in production, so it never sits in a file that could be committed.
/// </summary>
public sealed class VaBaseSettings
{
    public const string Section = "VaBase";

    /// <summary>Your airline's issuer, for example https://portal.vabase.com/sso/fleet-air.</summary>
    public string Authority { get; set; } = "";

    /// <summary>The client id from the application's page in the Admin Portal (vbapp_...).</summary>
    public string ClientId { get; set; } = "";

    /// <summary>The client secret shown once when the application was registered (vbas_...).</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>Your airline's slug, for example fleet-air. Checked against the vabase_airline claim.</summary>
    public string Airline { get; set; } = "";

    /// <summary>The optional scopes to ask for: profile, email, vabase.role. openid is always added.
    /// Ask only for what the airline ticked when it registered the application, or vaBase refuses the sign-in.</summary>
    public string[] Scopes { get; set; } = [];

    /// <summary>The permission area the restricted page needs, for example ReviewPireps. Needs vabase.role.</summary>
    public string RequiredArea { get; set; } = "";

    /// <summary>How long this application's own session lasts before the person goes back through vaBase.</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(1);

    public IEnumerable<string> AllScopes() => Scopes.Prepend("openid").Distinct(StringComparer.Ordinal);

    /// <summary>Stops the application at start-up with a clear message, rather than failing at the first sign-in.</summary>
    public void Validate()
    {
        var problems = new List<string>();
        if (!Uri.TryCreate(Authority, UriKind.Absolute, out var authority) || authority.Scheme != Uri.UriSchemeHttps)
            problems.Add("VaBase:Authority must be your airline's https issuer, such as https://portal.vabase.com/sso/fleet-air.");
        else if (!Authority.TrimEnd('/').EndsWith("/sso/" + Airline, StringComparison.Ordinal))
            problems.Add("VaBase:Authority and VaBase:Airline name different airlines. The issuer ends with /sso/ and the airline's slug.");
        if (string.IsNullOrWhiteSpace(Airline))
            problems.Add("VaBase:Airline must be your airline's slug, such as fleet-air.");
        if (!ClientId.StartsWith("vbapp_", StringComparison.Ordinal) || ClientId.Contains("REPLACE", StringComparison.Ordinal))
            problems.Add("VaBase:ClientId must be the client id from the application's page in the Admin Portal.");
        if (string.IsNullOrWhiteSpace(ClientSecret))
            problems.Add("VaBase:ClientSecret is not set. While developing, run: dotnet user-secrets set \"VaBase:ClientSecret\" \"vbas_...\"");
        if (string.IsNullOrWhiteSpace(RequiredArea))
            problems.Add("VaBase:RequiredArea must name a permission area, such as ReviewPireps.");
        if (SessionLifetime <= TimeSpan.Zero)
            problems.Add("VaBase:SessionLifetime must be positive, such as 01:00:00.");
        if (problems.Count > 0)
            throw new InvalidOperationException("The VaBase settings are incomplete:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }
}
