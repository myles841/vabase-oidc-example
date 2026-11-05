// An airline's own application signing its owners and staff in with Login with vaBase.
//
// vaBase is a standard OpenID Connect provider, so this uses Microsoft's ordinary OpenID Connect handler.
// The only vaBase-specific code is reading the vabase_airline, role and vabase_areas claims.
//
// The flow: a page that needs a signed-in person sends them to vaBase (the authorization code flow with
// PKCE). They come back to /signin-oidc with a code, which the handler exchanges for tokens on this
// server using the client secret. The handler validates the ID token, and this application then keeps
// its OWN session in a cookie. vaBase is not involved again until that session ends or a re-check runs.

using System.Security.Claims;
using LoginWithVaBase;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);

var settings = builder.Configuration.GetSection(VaBaseSettings.Section).Get<VaBaseSettings>() ?? new VaBaseSettings();
settings.Validate();
builder.Services.AddSingleton(settings);
builder.Services.AddAntiforgery();

builder.Services
    .AddAuthentication(options =>
    {
        // The application's own session is a cookie; signing in is done by vaBase.
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "login-with-vabase-example";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        // Keep this session short. vaBase cannot end it: someone removed from the airline's team stays
        // signed in here until it expires or the application re-checks with vaBase. When it expires, the
        // next page sends the person through vaBase again, which is silent if they are still allowed.
        options.ExpireTimeSpan = settings.SessionLifetime;
        options.SlidingExpiration = false;
        options.AccessDeniedPath = "/denied";
    })
    .AddOpenIdConnect(options =>
    {
        // Discovery is read from {Authority}/.well-known/openid-configuration: the endpoints and the
        // signing keys. The handler then checks the ID token's issuer is exactly this airline's issuer.
        options.Authority = settings.Authority;
        options.ClientId = settings.ClientId;
        options.ClientSecret = settings.ClientSecret;

        // The authorization code flow with PKCE (S256), which is all vaBase accepts. The answer comes
        // back in the query string; vaBase checks the sign-in address against the registered list exactly,
        // so register https://<this host>/signin-oidc (the handler's default CallbackPath).
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.ResponseMode = OpenIdConnectResponseMode.Query;
        options.UsePkce = true;

        options.Scope.Clear();
        foreach (var scope in settings.AllScopes())
            options.Scope.Add(scope);

        // Keep the claim names exactly as vaBase sends them (sub, name, role, vabase_areas), rather than
        // translating them to long Microsoft claim types.
        options.MapInboundClaims = false;
        options.TokenValidationParameters.NameClaimType = "name";
        options.TokenValidationParameters.RoleClaimType = "role";

        // Everything needed is in the ID token, so the user info endpoint is not called, and the tokens are
        // not kept: once the ID token is validated this application only needs its own session.
        options.GetClaimsFromUserInfoEndpoint = false;
        options.SaveTokens = false;
        // The session lasts SessionLifetime, not the five minutes the ID token lives.
        options.UseTokenLifetime = false;

        options.Events.OnTokenValidated = context =>
        {
            // Defence in depth. Issuer validation already ties the token to this airline; this second
            // check still holds if the Authority setting is ever pointed at the wrong airline.
            var airline = context.Principal?.FindFirst("vabase_airline")?.Value;
            if (!string.Equals(airline, settings.Airline, StringComparison.Ordinal))
                context.Fail("The sign-in is for a different airline.");
            return Task.CompletedTask;
        };

        options.Events.OnRemoteFailure = async context =>
        {
            var http = context.HttpContext;
            var error = context.Failure?.Data["error"] as string;
            http.RequestServices.GetRequiredService<ILogger<VaBaseSettings>>()
                .LogWarning(context.Failure, "Signing in with vaBase did not complete ({Error}).", error ?? "no error code");

            // The three answers to a prompt=none re-check: the person is signed out of vaBase, is no
            // longer allowed, or has not confirmed this application. End this application's session too.
            if (error is "login_required" or "interaction_required" or "consent_required")
            {
                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                http.Response.Redirect("/?recheck=" + Uri.EscapeDataString(error));
            }
            else
            {
                http.Response.Redirect("/problem?error=" + Uri.EscapeDataString(error ?? "unknown"));
            }
            context.HandleResponse();
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Open only to people who hold the configured permission area. Owners carry every area in
    // vabase_areas, so they pass without a special case. The claim arrives only with the vabase.role scope.
    options.AddPolicy("HoldsRequiredArea", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("vabase_areas", settings.RequiredArea));
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseHsts();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", (HttpContext http, IAntiforgery antiforgery, string? recheck) =>
    Pages.Home(http.User, settings, antiforgery.GetAndStoreTokens(http), recheck));

// The start address. Register it on the application's page so a custom link in the Admin Portal can open
// it: it begins an ordinary sign-in, or shows the person in if they already have a session here.
app.MapGet("/signin", (HttpContext http) => http.User.Identity?.IsAuthenticated == true
    ? Results.Redirect("/")
    : Results.Challenge(new AuthenticationProperties { RedirectUri = "/who" }, [OpenIdConnectDefaults.AuthenticationScheme]));

// A silent re-check: through vaBase with prompt=none. If the person is still allowed they come straight
// back with a fresh ID token (today's role and areas); if not, OnRemoteFailure ends the session here.
// For a fresh sign-in before something sensitive, set MaxAge instead, for example TimeSpan.FromMinutes(5).
app.MapGet("/recheck", () =>
    Results.Challenge(new OpenIdConnectChallengeProperties { RedirectUri = "/who", Prompt = "none" },
        [OpenIdConnectDefaults.AuthenticationScheme]));

// Signs out of THIS application only. vaBase has no sign-out endpoint for applications, so the person stays
// signed in to vaBase, and signing in here again brings them straight back.
app.MapPost("/signout", async (HttpContext http, IAntiforgery antiforgery) =>
{
    if (!await antiforgery.IsRequestValidAsync(http))
        return Results.BadRequest();
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
});

app.MapGet("/who", async (HttpContext http) =>
{
    var session = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Pages.Who(http.User, session.Properties);
}).RequireAuthorization();

app.MapGet("/restricted", (ClaimsPrincipal user) => Pages.Restricted(user, settings))
    .RequireAuthorization("HoldsRequiredArea");

app.MapGet("/denied", (ClaimsPrincipal user) => Pages.Denied(user, settings));

app.MapGet("/problem", (string? error) => Pages.Problem(error));

app.Run();
