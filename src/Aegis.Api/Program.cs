using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Aegis.Api.Authorization;
using Aegis.Api.Middleware;
using Aegis.Application;
using Aegis.Infrastructure;
using Aegis.Infrastructure.Persistence;
using Aegis.Infrastructure.Security;
using Aegis.Infrastructure.Security.Tokens;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Threading.RateLimiting;
using Serilog;
using Serilog.Exceptions;

// A bootstrap logger so that failures during startup, before configuration is read, are still
// recorded. Without it, a bad connection string produces a silent crash with no explanation.
//
// InvariantCulture is not ceremony here: logs formatted in the host's locale render dates and
// numbers differently per machine, which breaks log parsing the moment one node has a different
// regional setting from the rest.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId()
        .Enrich.WithExceptionDetails());

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    builder.Services
        .AddControllers()
        .AddJsonOptions(options =>
        {
            // Enums as strings. An integer in a payload is meaningless to anyone reading it, and
            // reordering an enum silently changes the meaning of every stored and logged value.
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

    // Suppresses the automatic 400 from [ApiController] model binding so that every error in the
    // API has one shape, produced by ResultExtensions, rather than two that clients must both
    // handle.
    builder.Services.Configure<ApiBehaviorOptions>(options =>
        options.SuppressModelStateInvalidFilter = false);

    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();

    // ---- Authentication ----

    var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
        ?? throw new InvalidOperationException(
            "The Jwt configuration section is missing. The API refuses to start without it rather " +
            "than fall back to a default signing key.");

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwt.Issuer,

                ValidateAudience = true,
                ValidAudience = jwt.Audience,

                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),

                ValidateLifetime = true,

                // Default is five minutes, which silently extends every token's life by that much.
                // Zero makes the configured lifetime mean what it says.
                ClockSkew = TimeSpan.Zero,

                // The claim names Aegis actually issues. Without these, ASP.NET Core maps to the
                // SOAP-era URI claim types and both ICurrentUser and the permission handler see
                // nothing.
                NameClaimType = AegisClaims.Email,
                RoleClaimType = AegisClaims.Role,
            };

            // Inbound claim mapping rewrites short names such as "sub" into long URIs. Turning it
            // off keeps what arrives identical to what was issued.
            options.MapInboundClaims = false;

            options.Events = new JwtBearerEvents
            {
                // Closes the window in which a revoked capability keeps working. A signature-valid
                // token is not sufficient: the security stamp it carries must still match the
                // user's current one, so a password change, a role withdrawal or a deactivation
                // takes effect on the next request rather than when the token happens to expire.
                //
                // Backed by a cache with explicit eviction, so the steady-state cost is one cache
                // read rather than a database round trip per request.
                OnTokenValidated = async context =>
                {
                    var principal = context.Principal;

                    if (principal is null ||
                        !Guid.TryParse(principal.FindFirst(AegisClaims.UserId)?.Value, out var userId))
                    {
                        context.Fail("The token carries no usable subject claim.");
                        return;
                    }

                    var stampService = context.HttpContext.RequestServices
                        .GetRequiredService<Aegis.Application.Abstractions.Security.ISecurityStampService>();

                    var stamp = principal.FindFirst(AegisClaims.SecurityStamp)?.Value;

                    if (!await stampService.IsCurrentAsync(userId, stamp, context.HttpContext.RequestAborted))
                    {
                        context.Fail("The session is no longer valid.");
                    }
                },

                OnAuthenticationFailed = context =>
                {
                    // Tells the client specifically that the token expired, so it can refresh
                    // rather than bounce the user to a login screen.
                    if (context.Exception is SecurityTokenExpiredException)
                    {
                        context.Response.Headers.Append("X-Token-Expired", "true");
                    }

                    return Task.CompletedTask;
                },

                OnMessageReceived = context =>
                {
                    // SignalR cannot set an Authorization header on the WebSocket handshake, so
                    // hub connections pass the token as a query parameter. Accepted only for hub
                    // paths: honouring it everywhere would put access tokens into server logs,
                    // browser history and referrer headers.
                    var accessToken = context.Request.Query["access_token"];

                    if (!string.IsNullOrEmpty(accessToken) &&
                        context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }

                    return Task.CompletedTask;
                },
            };
        });

    // ---- Authorization ----

    builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
    builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

    // ---- Rate limiting ----
    //
    // Account lockout defends one account against many guesses. It is blind to the opposite shape
    // of attack: credential spraying, where one common password is tried against thousands of
    // accounts. Each account sees a single failure, no lockout ever triggers, and a few percent of
    // accounts fall. Limiting by source address is what catches that.
    var rateLimits = builder.Configuration.GetSection(RateLimitOptions.SectionName)
        .Get<RateLimitOptions>() ?? new RateLimitOptions();

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.OnRejected = async (context, cancellationToken) =>
        {
            // Retry-After turns a rejection into something a well-behaved client can act on, rather
            // than something it retries immediately and makes worse.
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                context.HttpContext.Response.Headers.RetryAfter =
                    ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
            }

            context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("Aegis.RateLimiter")
                .LogWarning(
                    "Rate limit rejected {Method} {Path} from {IpAddress}",
                    context.HttpContext.Request.Method,
                    context.HttpContext.Request.Path,
                    context.HttpContext.Connection.RemoteIpAddress);

            await context.HttpContext.Response.WriteAsJsonAsync(
                new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too many requests.",
                    Detail = "Slow down and try again shortly.",
                },
                cancellationToken);
        };

        // Applied to the authentication endpoints, which are anonymous and therefore the ones an
        // attacker can hammer without holding an account.
        options.AddPolicy(RateLimitPolicies.Authentication, httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                // Partitioned by source address. Partitioning by email instead would let an
                // attacker sidestep the limit entirely by varying the address, which is precisely
                // what spraying does.
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimits.AuthenticationPermitLimit,
                    Window = TimeSpan.FromSeconds(rateLimits.AuthenticationWindowSeconds),

                    // No queue. Holding a failed sign-in attempt open consumes a connection for an
                    // attacker's benefit; rejecting immediately is both cheaper and clearer.
                    QueueLimit = 0,
                }));

        // A deliberately tighter budget for registration. It creates a tenant, seeds five roles and
        // performs a full key derivation, so it is the most expensive anonymous endpoint in the API
        // and the obvious lever for exhausting the database.
        options.AddPolicy(RateLimitPolicies.Registration, httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimits.RegistrationPermitLimit,
                    Window = TimeSpan.FromSeconds(rateLimits.RegistrationWindowSeconds),
                    QueueLimit = 0,
                }));
    });

    builder.Services.AddAuthorizationBuilder()
        // Secure by default: an endpoint with no attribute requires authentication rather than
        // being public. Forgetting [Authorize] then fails closed, and [AllowAnonymous] becomes an
        // explicit, reviewable decision.
        .SetFallbackPolicy(new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build());

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Aegis Infrastructure Management API",
            Version = "v1",
            Description =
                "Operations platform for utilities and municipal infrastructure authorities: " +
                "asset registry, incident intake, work orders and predictive maintenance.",
        });

        // Pulls the XML documentation from the assemblies into Swagger, so the reasoning written
        // beside the code becomes the reasoning shown in the API reference.
        foreach (var xml in Directory.GetFiles(AppContext.BaseDirectory, "Aegis.*.xml"))
        {
            options.IncludeXmlComments(xml, includeControllerXmlComments: true);
        }

        // Lets Swagger UI carry a bearer token, so the documentation is usable for exploration
        // rather than only for reading.
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Paste the access token returned by /api/v1/auth/login.",
        });

        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer",
                },
            }] = [],
        });
    });

    builder.Services
        .AddHealthChecks()
        .AddDbContextCheck<AegisDbContext>("database", tags: ["ready"]);

    var app = builder.Build();

    // ---- Pipeline. Order here is behaviour, not preference. ----

    // Populates RemoteIpAddress from the proxy's forwarded headers. Must precede anything that
    // reads the client address, which includes the audit trail.
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    });

    // Before Serilog's request logging, so that every request log line carries the correlation id.
    app.UseMiddleware<CorrelationIdMiddleware>();

    app.UseSerilogRequestLogging(options =>
        options.GetLevel = (httpContext, elapsed, exception) => exception is not null
            ? Serilog.Events.LogEventLevel.Error
            : httpContext.Response.StatusCode >= 500
                ? Serilog.Events.LogEventLevel.Error
                : Serilog.Events.LogEventLevel.Information);

    app.UseExceptionHandler();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Aegis API v1");
            options.DocumentTitle = "Aegis API";
        });
    }
    else
    {
        app.UseHsts();
    }

    app.UseHttpsRedirection();

    // Before authentication, deliberately. A rejected request should cost as little as possible,
    // and validating a JWT signature for a caller who is about to be turned away is work done on
    // the attacker's behalf.
    app.UseRateLimiter();

    app.UseAuthentication();

    // After authentication: the organization claim does not exist until the principal is built.
    app.UseMiddleware<TenantResolutionMiddleware>();

    app.UseAuthorization();

    app.MapControllers();

    // Liveness: is the process running? Deliberately checks nothing else, so a database blip
    // cannot cause an orchestrator to kill an otherwise healthy instance.
    // AllowAnonymous is required because the fallback policy demands authentication. A probe that
    // needs a token is a probe an orchestrator cannot call, and the instance is then marked
    // unhealthy for the one reason that has nothing to do with its health.
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false,
    }).AllowAnonymous();

    // Readiness: can this instance actually serve traffic? Checks dependencies, so a instance
    // that cannot reach SQL Server is removed from the load balancer rather than serving errors.
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
    }).AllowAnonymous();

    await app.RunAsync();
}
catch (Exception exception) when (exception is not HostAbortedException)
{
    Log.Fatal(exception, "Aegis API terminated unexpectedly during startup");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>
/// Exposed so that <c>WebApplicationFactory&lt;Program&gt;</c> in the integration test project can
/// locate the entry point; top-level statements otherwise compile to an internal class.
/// </summary>
[ApiExplorerSettings(IgnoreApi = true)]
public partial class Program;
