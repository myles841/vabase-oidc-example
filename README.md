# Login with vaBase: ASP.NET Core example

A small, working ASP.NET Core application that signs a vaBase airline's **Owners and Staff** in with their existing vaBase account, through **Login with vaBase**.

vaBase is a standard **OpenID Connect** provider, so this example uses Microsoft's ordinary OpenID Connect handler. Nothing in it is vaBase-specific except reading three claims: `vabase_airline`, `role` and `vabase_areas`. You can use any maintained OpenID Connect library in any language in the same way.

The full developer guide is on the **Developer** page of the vaBase Admin Portal (Integrations › Developer › Sign in with vaBase), with a downloadable copy.

[![Build](https://github.com/myles841/vabase-oidc-example/actions/workflows/build.yml/badge.svg)](https://github.com/myles841/vabase-oidc-example/actions/workflows/build.yml)

## Contents

1. [How it works, in one minute](#how-it-works-in-one-minute)
2. [What you need](#what-you-need)
3. [Step 1: register an application in vaBase](#step-1-register-an-application-in-vabase)
4. [Step 2: configure the redirect URI](#step-2-configure-the-redirect-uri)
5. [Step 3: get the client id and secret](#step-3-get-the-client-id-and-secret)
6. [Step 4: configure this example](#step-4-configure-this-example)
7. [Step 5: run it locally over HTTPS](#step-5-run-it-locally-over-https)
8. [Step 6: sign in with vaBase](#step-6-sign-in-with-vabase)
9. [Step 7: read the verified identity](#step-7-read-the-verified-identity)
10. [Step 8: authorise in your own application](#step-8-authorise-in-your-own-application)
11. [Sessions, sign-out and permission changes](#sessions-sign-out-and-permission-changes)
12. [When vaBase refuses a sign-in](#when-vabase-refuses-a-sign-in)
13. [Security notes](#security-notes)
14. [Reference: issuer, endpoints, scopes and claims](#reference-issuer-endpoints-scopes-and-claims)

## How it works, in one minute

1. A page in **your** application needs a signed-in person, so it sends their browser to your airline's vaBase issuer.
2. On vaBase they sign in with their vaBase password **and a code from their authenticator app** (every Login with vaBase sign-in needs two-factor sign-in). The first time, they confirm what your application will be told.
3. vaBase sends their browser back to your application's **redirect URI** with a one-time **code**.
4. Your **server** exchanges the code for an **ID token**, using your **client secret** and PKCE. The code works once and lives for one minute.
5. Your server **validates** the ID token: signature (against vaBase's published keys), issuer, audience, expiry and nonce. Microsoft's handler does all of this.
6. Your application creates **its own session** (a cookie) and decides, from the claims, what the person may do.

vaBase decides **who may sign in** to your application (only the airline's Owners and Staff, and only those the airline allows). Your application decides **what they may do once signed in**. Pilots never sign in through Login with vaBase.

## What you need

- The **.NET 10 SDK**.
- A trusted HTTPS development certificate: `dotnet dev-certs https --trust`. vaBase accepts only `https://` redirect URIs, including during development.
- A vaBase airline where you are an **Owner**, or a **Staff** member holding the **Developer access** permission area, to register the application.
- **Two-factor sign-in turned on** for the vaBase account you will sign in with. Set it up in the vaBase Account Portal under **Security**.

## Step 1: register an application in vaBase

1. Sign in to the vaBase **Admin Portal** and choose your airline.
2. Open **Integrations › Developer** and go to **Applications**.
3. Press **Register an application** and fill in:
   - **Name:** for example `Crew Training`. People see it on vaBase's confirmation screen.
   - **Sign-in addresses:** see step 2.
   - **Start address** (optional): `https://localhost:7215/signin`. A Custom link in the Admin Portal sidebar can open this address to start a sign-in.
   - **Who can sign in:** Everyone on the team, Owners only, or Staff with chosen permission areas. vaBase checks this at every sign-in.
   - **What the application is told:** tick **Their name**, **Their email address** and **Their role and permission areas** to try everything this example shows.
4. Press **Register application**.

## Step 2: configure the redirect URI

Add this **exact** sign-in address (the redirect URI) to the application:

```
https://localhost:7215/signin-oidc
```

`https://localhost:7215` is this example's address on your computer (set in `Properties/launchSettings.json`), and `/signin-oidc` is where Microsoft's handler receives the answer. vaBase compares redirect URIs **exactly**: scheme, host, port, path, case and trailing slash all count, and there are no wildcards. In production, register your application's real `https://` address, for example `https://training.your-airline.example/signin-oidc`.

## Step 3: get the client id and secret

After registering, vaBase shows:

- the **client id** (`vbapp_...`): not a secret; it identifies your application;
- the **client secret** (`vbas_...`): **shown once only**. Copy it now. If it is lost, rotate it on the application's page;
- the **discovery address**: `https://portal.vabase.com/sso/{your-airline-slug}/.well-known/openid-configuration`.

## Step 4: configure this example

`appsettings.json` contains placeholders only. Replace them:

```json
"VaBase": {
  "Authority": "https://portal.vabase.com/sso/your-airline-slug",
  "Airline": "your-airline-slug",
  "ClientId": "vbapp_...",
  "Scopes": [ "profile", "email", "vabase.role" ],
  "RequiredArea": "ReviewPireps",
  "SessionLifetime": "01:00:00"
}
```

| Setting | Meaning |
|---|---|
| `Authority` | Your airline's **issuer**: the discovery address without `/.well-known/openid-configuration`, and with no trailing slash. |
| `Airline` | Your airline's slug. The example refuses a sign-in whose `vabase_airline` claim is anything else. |
| `ClientId` | The client id from step 3. |
| `Scopes` | The optional scopes to ask for. `openid` is always added. Ask only for what the airline ticked. |
| `RequiredArea` | The permission area the `/restricted` page needs, such as `ReviewPireps`, `ManageFleet` or `ManageSchedules`. Needs the `vabase.role` scope. |
| `SessionLifetime` | How long this application's own session lasts before the person goes back through vaBase. |

**Never put the client secret in `appsettings.json` or any file you commit.** During development use .NET User Secrets, from the folder that holds `LoginWithVaBase.csproj`:

```
dotnet user-secrets set "VaBase:ClientSecret" "vbas_..."
```

or an environment variable (PowerShell shown; use `export` on macOS or Linux):

```
$env:VaBase__ClientSecret = "vbas_..."
```

In production, supply it from your host's secret store (for example Azure Key Vault or your platform's secrets) as `VaBase__ClientSecret`. The example stops at start-up and lists anything that is missing or still a placeholder.

## Step 5: run it locally over HTTPS

```
dotnet dev-certs https --trust
dotnet run
```

Open **https://localhost:7215**.

## Step 6: sign in with vaBase

Press **Sign in with vaBase** (or open `/signin`, the start address). This is the whole of the code that starts a sign-in:

```csharp
app.MapGet("/signin", (HttpContext http) => http.User.Identity?.IsAuthenticated == true
    ? Results.Redirect("/")
    : Results.Challenge(new AuthenticationProperties { RedirectUri = "/who" },
        [OpenIdConnectDefaults.AuthenticationScheme]));
```

You are sent to vaBase. Sign in with your password and your authenticator code, confirm the application the first time, and you come back to **Who signed in**. The handler set-up is in `Program.cs`: the code flow, PKCE, `MapInboundClaims = false` (so claims keep vaBase's names), and `SaveTokens = false` (the tokens are not kept; this application only needs its own session).

## Step 7: read the verified identity

`/who` lists every claim from the validated ID token. Read them from `HttpContext.User`:

```csharp
var subject  = user.FindFirst("sub")?.Value;             // stable for this person at this airline only
var airline  = user.FindFirst("vabase_airline")?.Value;  // your airline's slug
var name     = user.FindFirst("name")?.Value;            // with the profile scope
var email    = user.FindFirst("email")?.Value;           // with the email scope
var role     = user.FindFirst("role")?.Value;            // "Owner" or "Staff", with vabase.role
var areas    = user.FindAll("vabase_areas").Select(c => c.Value); // permission areas, with vabase.role
```

Key your own user records on **`sub`**, never on the email address (which a person can change). `sub` is different at every airline, so it cannot be used to follow someone across airlines.

The example also checks the airline, as defence in depth beside the issuer check the handler already makes:

```csharp
options.Events.OnTokenValidated = context =>
{
    if (context.Principal?.FindFirst("vabase_airline")?.Value != settings.Airline)
        context.Fail("The sign-in is for a different airline.");
    return Task.CompletedTask;
};
```

## Step 8: authorise in your own application

vaBase tells you **who** someone is and their role and permission areas **at the moment they signed in**. Your application decides what they may do. This example's policy opens `/restricted` only to holders of one permission area; Owners carry every area, so they pass:

```csharp
builder.Services.AddAuthorization(options =>
    options.AddPolicy("HoldsRequiredArea", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("vabase_areas", settings.RequiredArea)));

app.MapGet("/restricted", ...).RequireAuthorization("HoldsRequiredArea");
```

Make every such decision **on your server**. Hiding a button is not authorisation; a vaBase Custom link being hidden from someone is not authorisation either.

## Sessions, sign-out and permission changes

- **Your session is your own.** It lasts `SessionLifetime` (an hour in this example). When it ends, the next page goes back through vaBase, which is silent if the person is still allowed.
- **Permission changes:** the role and areas are a snapshot from sign-in. To refresh them, re-check with vaBase. `/recheck` sends the person through vaBase with `prompt=none`: if they are still allowed they come straight back with today's role and areas; if not, vaBase answers `login_required`, `interaction_required` or `consent_required`, and the example ends its own session.
- **A fresh sign-in before something sensitive:** set `MaxAge` on the challenge (for example five minutes). vaBase then asks for the password and code again if the person signed in longer ago than that.
- **Sign-out:** the example's Sign out ends **this application's** session only. vaBase has no sign-out endpoint for applications, so the person stays signed in to vaBase.
- **Removing someone in vaBase** stops every **new** sign-in at once, but cannot end a session your application already holds. Keep sessions short and re-check regularly.

## When vaBase refuses a sign-in

For these, vaBase shows the person a page of its own and your application receives **nothing**:

| What happened | What the person sees on vaBase |
|---|---|
| Not on the airline's team (never, or removed) | "You are not on {airline}'s team" |
| "Who can sign in" does not include them | "{application} isn't open to your account" |
| The application is **disabled** | "{application} is switched off" |
| The airline is **suspended** | "{airline}'s applications are paused" |
| Their account has no two-factor sign-in, or their session began without a code | "{application} needs two-factor sign-in" / "Sign in again to continue" |
| An unknown client id, or a redirect URI that is not registered exactly | "This sign-in could not be started", with a reference for vaBase support |

What can reach your application (this example's `/problem` page explains each):

| `error` | Meaning |
|---|---|
| `invalid_grant` (at the code exchange) | The person was removed, the application disabled or the airline suspended **between** signing in and the exchange, or the code was used twice or after a minute. Nothing about them is shared. |
| `invalid_client` | Wrong or rotated client secret, or the client id is not known at this issuer. Repeated failures from one address are paused for 15 minutes. |
| `invalid_request`, `invalid_scope`, `unsupported_response_type` (at your redirect URI) | The request did not match the registration, for example a scope the airline has not ticked. |
| `login_required`, `interaction_required`, `consent_required` | The answers to a `prompt=none` re-check. |

## Security notes

- The **client secret** belongs on your server only: never in browser JavaScript, a mobile or desktop app, a log, a screenshot or a source repository. If it may have leaked, rotate it and stop the old one at once.
- This example uses the **authorization code flow with PKCE (S256)**, which is all vaBase accepts. Implicit and hybrid flows, and PKCE `plain`, are refused.
- Tokens are not stored (`SaveTokens = false`). The application only needs its own cookie, which is `HttpOnly`, `Secure` and `SameSite=Lax`.
- A real application also needs Data Protection keys that persist across restarts and servers (they protect the session cookie), and the forwarded-headers middleware when it runs behind a proxy, so the redirect URI it sends is its public `https://` address.

## Reference: issuer, endpoints, scopes and claims

| | |
|---|---|
| Issuer | `https://portal.vabase.com/sso/{airline-slug}` |
| Discovery | `{issuer}/.well-known/openid-configuration` |
| Authorization | `{issuer}/connect/authorize` |
| Token | `{issuer}/connect/token` |
| User info | `{issuer}/connect/userinfo` |
| Keys (JWKS) | `{issuer}/.well-known/jwks` |
| Flow | Authorization code with PKCE (`S256`), confidential clients only |
| Lifetimes | Code 1 minute; ID and access tokens 5 minutes; no refresh tokens |

| Scope | Claims |
|---|---|
| `openid` (always) | `sub`, `vabase_airline`, `amr` (`pwd`, `otp`, `mfa`), `auth_time` |
| `profile` | `name` |
| `email` | `email`, `email_verified` |
| `vabase.role` | `role` (`Owner` or `Staff`), `vabase_areas` (an array of permission area names) |

Each airline is its own issuer. A token from one airline's issuer is never valid at another's, so validating the issuer, as Microsoft's handler does, keeps airlines apart.

## Licence

[MIT](LICENSE).
