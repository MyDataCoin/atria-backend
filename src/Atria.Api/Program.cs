using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Atria.Api.Middleware;
using Atria.Api.Security;
using Atria.Application.Abstractions;
using Atria.Infrastructure;
using Atria.Infrastructure.Configuration;
using Atria.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// --- Serilog host logger: console only, enriched with the request CorrelationId.
//     The output template is deliberately PII-free (no OTP/token/password/email fields). ---
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}"));

// --- Infrastructure composition root: DbContext, validated options, repositories,
//     mediator + pipeline, domain-event dispatcher, strategies/adapters, identity,
//     compliance, hosted services, and the Application handlers + validators. ---
builder.Services.AddInfrastructure(builder.Configuration);

// --- Current user adapter (reads claims off the HTTP context for handler authz). ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// --- Forwarded headers so RemoteIpAddress is the real client IP behind a proxy
//     (used for OTP request rate limiting / abuse capture). ---
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // KnownNetworks/KnownProxies are cleared so X-Forwarded-* is trusted from the ingress.
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

// --- MVC controllers. Validators are registered by Infrastructure and run inside the
//     Application mediator pipeline (ValidationBehavior), so no MVC auto-validation hook
//     is needed here. Enums are (de)serialized by NAME (e.g. "Didit") rather than integers
//     — clearer payloads and Swagger lists the allowed values. ---
builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// --- API versioning: default v1.0, URL segment reader, grouped explorer for Swagger. ---
builder.Services
    .AddApiVersioning(o =>
    {
        o.DefaultApiVersion = new ApiVersion(1, 0);
        o.AssumeDefaultVersionWhenUnspecified = true;
        o.ReportApiVersions = true;
        o.ApiVersionReader = new UrlSegmentApiVersionReader();
    })
    .AddApiExplorer(o =>
    {
        o.GroupNameFormat = "'v'VVV";
        o.SubstituteApiVersionInUrl = true;
    });

// --- JWT bearer authentication from JwtOptions (Issuer / Audience / SigningKey). ---
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
          ?? new JwtOptions();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep inbound claims as-issued: tokens carry NameIdentifier/email and a role
        // claim emitted as the Role enum NAME, so [Authorize(Roles=...)] + CurrentUserService work.
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            // Map the JWT 'role' claim to the role claim type so [Authorize(Roles=...)] matches.
            RoleClaimType = "role",
            NameClaimType = "sub"
        };
    });

// --- Role policies: Admin / Investor / Compliance. ---
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Admin", p => p.RequireRole("Admin"))
    .AddPolicy("Investor", p => p.RequireRole("Investor"))
    .AddPolicy("Compliance", p => p.RequireRole("Compliance"));

// --- Swagger / OpenAPI with a JWT bearer security scheme, grouped by API version. ---
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Atria API",
        Version = "v1",
        Description =
            "Real-estate tokenization (RWA) platform API.\n\n" +
            "**Authentication is phone-first (Kyrgyzstan +996 numbers).** Register/sign in via " +
            "`/auth/register/phone/request-otp` then `/auth/register/phone/verify-otp` to receive a JWT. " +
            "Email/password endpoints also exist. Send the access token as `Authorization: Bearer {token}`.\n\n" +
            "Enums are sent/returned by name (e.g. `Didit`, `Stripe`, `Investor`); each schema lists its allowed values. " +
            "Errors come back as RFC-7807 ProblemDetails with a `correlationId`."
    });

    // Pull in the XML doc comments from every layer so summaries/remarks/param docs and
    // enum/DTO descriptions show up in the UI. (Generated via GenerateDocumentationFile.)
    foreach (var xml in new[] { "Atria.Api.xml", "Atria.Application.xml", "Atria.Domain.xml" })
    {
        var path = Path.Combine(AppContext.BaseDirectory, xml);
        if (File.Exists(path))
            c.IncludeXmlComments(path, includeControllerXmlComments: true);
    }

    c.SupportNonNullableReferenceTypes();
    c.UseAllOfToExtendReferenceSchemas();

    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the JWT access token only (Swagger adds the 'Bearer ' prefix).",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };
    c.AddSecurityDefinition("Bearer", scheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = Array.Empty<string>() });
});

// --- Health checks: liveness (self) + readiness (Postgres reachable). ---
builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("Postgres") ?? string.Empty,
        name: "postgres",
        tags: new[] { "ready" });

// --- Rate limiting: throttle ONLY the auth + OTP endpoints (login, register, request-otp)
//     to slow brute force / SMS abuse. All other paths are not partitioned (no limit). ---
string[] throttledPaths =
{
    "/api/v1/auth/login",
    "/api/v1/auth/register",
    "/api/v1/auth/register/phone/request-otp"
};
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var path = httpContext.Request.Path.Value ?? string.Empty;
        var isThrottled = throttledPaths.Any(p =>
            path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

        if (!isThrottled)
        {
            return RateLimitPartition.GetNoLimiter("__unlimited");
        }

        // Partition by client IP + path so one caller cannot starve others.
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter($"{ip}:{path}", _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
    });
});

var app = builder.Build();

// --- Apply EF migrations on startup when explicitly enabled (e.g. docker-compose) so the
//     containerized stack comes up with a ready schema. OFF by default — never auto-migrate
//     a production database you do not control; run `dotnet ef database update` there. ---
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AtriaDbContext>();
    await db.Database.MigrateAsync();
}

// --- Middleware pipeline ---
// Exception handling wraps everything; correlation id is set before errors are written
// so the id appears in problem bodies and every log line.
app.UseExceptionHandling();
app.UseCorrelationId();
app.UseSecurityHeaders();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseHttpsRedirection();

// Swagger is exposed only in Development.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Atria API v1"));
}

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Global limiter only partitions the auth/OTP paths; everything else is unlimited.
app.UseRateLimiter();

app.MapControllers();

// Health endpoints (no auth): liveness = self only, readiness = Postgres tagged "ready".
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();

/// <summary>Exposed so WebApplicationFactory&lt;Program&gt; can host the API in integration tests.</summary>
public partial class Program
{
    // Non-public ctor: top-level statements generate the entry point; this partial only
    // provides a referenceable type for the test host (and silences the empty-class rule).
    protected Program() { }
}
