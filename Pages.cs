using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;

namespace LoginWithVaBase;

/// <summary>
/// The example's pages, as plain HTML so the sign-in code stays the focus. Every value that came from a
/// token, a setting or the address bar is HTML-encoded before it is written into a page.
/// </summary>
public static class Pages
{
    private static string E(string? value) => HtmlEncoder.Default.Encode(value ?? "");

    public static IResult Home(ClaimsPrincipal user, VaBaseSettings settings, AntiforgeryTokenSet antiforgery, string? recheck)
    {
        var body = new StringBuilder();
        // Only known answers are shown; nothing from the address bar is written back as it was sent.
        var notice = recheck switch
        {
            "login_required" => "You are no longer signed in to vaBase, so you have been signed out here too.",
            "interaction_required" => "vaBase no longer lets this account sign in to this application, so you have been signed out here.",
            "consent_required" => "This application needs you to confirm it on vaBase again. Sign in to continue.",
            _ => null,
        };
        if (notice is not null)
            body.Append($"<p class=\"notice\">{E(notice)}</p>");

        if (user.Identity?.IsAuthenticated == true)
        {
            body.Append($"<p>Signed in as <strong>{E(user.Identity.Name ?? user.FindFirst("sub")?.Value)}</strong>.</p>");
            body.Append("<ul>");
            body.Append("<li><a href=\"/who\">Who signed in</a>: every claim vaBase sent.</li>");
            body.Append($"<li><a href=\"/restricted\">The {E(settings.RequiredArea)} page</a>: open only to holders of that permission area.</li>");
            body.Append("<li><a href=\"/recheck\">Re-check with vaBase</a>: a silent sign-in (<code>prompt=none</code>) that refreshes the role and areas, or signs you out here if vaBase no longer lets you in.</li>");
            body.Append("</ul>");
            body.Append($"""
                <form method="post" action="/signout">
                  <input type="hidden" name="{E(antiforgery.FormFieldName)}" value="{E(antiforgery.RequestToken)}" />
                  <button type="submit">Sign out of this application</button>
                </form>
                <p class="muted">Signing out here does not sign you out of vaBase.</p>
                """);
        }
        else
        {
            body.Append($"<p>This example signs {E(settings.Airline)}'s owners and staff in with their vaBase account.</p>");
            body.Append("<p><a class=\"button\" href=\"/signin\">Sign in with vaBase</a></p>");
        }
        return Page("Login with vaBase example", body.ToString());
    }

    public static IResult Who(ClaimsPrincipal user, AuthenticationProperties? session)
    {
        var body = new StringBuilder();
        // The handler validates iss, aud, exp and nonce, then leaves them out of the signed-in identity.
        // Each claim still records which issuer vouched for it.
        body.Append($"<p>Issued by <code>{E(user.FindFirst("sub")?.Issuer)}</code>.</p>");
        body.Append("<table><thead><tr><th>Claim</th><th>Value</th></tr></thead><tbody>");
        foreach (var claim in user.Claims)
            body.Append($"<tr><td><code>{E(claim.Type)}</code></td><td>{E(claim.Value)}</td></tr>");
        body.Append("</tbody></table>");

        if (long.TryParse(user.FindFirst("auth_time")?.Value, out var authTime))
            body.Append($"<p>Signed in to vaBase at {E(DateTimeOffset.FromUnixTimeSeconds(authTime).ToString("u"))}.</p>");
        if (session?.IssuedUtc is { } issued && session.ExpiresUtc is { } expires)
            body.Append($"<p>This application's own session began at {E(issued.ToString("u"))} and ends at {E(expires.ToString("u"))}.</p>");
        body.Append("""
            <p class="muted">The role and permission areas are a snapshot from the moment you signed in. If they change in
            vaBase, this application learns about it only when you next go through vaBase: when the session ends, or
            when you re-check.</p>
            <p><a href="/recheck">Re-check with vaBase</a> &middot; <a href="/">Home</a></p>
            """);
        return Page("Who signed in", body.ToString());
    }

    public static IResult Restricted(ClaimsPrincipal user, VaBaseSettings settings) => Page(
        $"{settings.RequiredArea} only",
        $"""
        <p>You can see this page because your <code>vabase_areas</code> claim includes <code>{E(settings.RequiredArea)}</code>.
        You are {(user.FindFirst("role")?.Value == "Owner" ? "an Owner, and Owners carry every area" : "a Staff member who holds this area")}.</p>
        <p class="muted">The decision is this application's own, made from the claims vaBase sent at sign-in.</p>
        <p><a href="/">Home</a></p>
        """);

    public static IResult Denied(ClaimsPrincipal user, VaBaseSettings settings) => Page(
        "Not open to you",
        $"""
        <p>This page needs the <code>{E(settings.RequiredArea)}</code> permission area, and your sign-in did not include it.</p>
        <p class="muted">{(user.HasClaim(c => c.Type == "role")
            ? "If an Owner has just given you the area, re-check with vaBase to pick it up."
            : "No role or areas arrived. Ask for the vabase.role scope, and make sure the airline ticked \"Their role and permission areas\" for this application.")}</p>
        <p><a href="/recheck">Re-check with vaBase</a> &middot; <a href="/">Home</a></p>
        """);

    // Most reasons a person may not sign in (not on the airline's team, the application disabled, the airline
    // suspended, two-factor sign-in not used) are explained on vaBase's own page and never reach this
    // application. What can reach it is below: a change between the sign-in and the code exchange, or a
    // configuration mistake in this application.
    public static IResult Problem(string? error) => Page(
        "Sign-in did not complete",
        $"""
        <p>Signing in with vaBase did not complete (<code>{E(error ?? "unknown")}</code>).</p>
        <p>{E(error switch
        {
            "invalid_grant" => "vaBase refused to finish the sign-in. The person was removed from the airline's team, the application was disabled, the airline was suspended, or the sign-in took longer than a minute. Nothing about them was shared.",
            "access_denied" => "The person chose not to continue.",
            "invalid_client" => "This application's client id or secret is wrong, or the secret was rotated. Check the configuration.",
            "invalid_request" or "invalid_scope" or "unsupported_response_type" => "The sign-in request did not match the registration: check the sign-in address, PKCE and that every scope asked for is ticked on the application's page.",
            _ => "The reason is in this application's log.",
        })}</p>
        <p><a href="/">Home</a></p>
        """);

    private static IResult Page(string title, string body) => Results.Content($$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <title>{{E(title)}}</title>
          <style>
            body { font-family: system-ui, sans-serif; max-width: 48rem; margin: 2rem auto; padding: 0 1rem; line-height: 1.5; color: #1a1a1a; }
            table { border-collapse: collapse; width: 100%; }
            th, td { text-align: left; padding: .4rem .6rem; border-bottom: 1px solid #ddd; vertical-align: top; word-break: break-word; }
            code { font-size: .95em; }
            .muted { color: #555; }
            .notice { background: #fff6d6; padding: .6rem .8rem; border-radius: .4rem; }
            .button { display: inline-block; background: #1f4fd6; color: #fff; padding: .5rem 1rem; border-radius: .4rem; text-decoration: none; }
          </style>
        </head>
        <body>
          <h1>{{E(title)}}</h1>
          {{body}}
        </body>
        </html>
        """, "text/html; charset=utf-8");
}
