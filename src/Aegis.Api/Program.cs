using System.Globalization;
using System.Text.Json.Serialization;
using Aegis.Api.Middleware;
using Aegis.Application;
using Aegis.Infrastructure;
using Aegis.Infrastructure.Persistence;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
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

    app.UseAuthentication();

    // After authentication: the organization claim does not exist until the principal is built.
    app.UseMiddleware<TenantResolutionMiddleware>();

    app.UseAuthorization();

    app.MapControllers();

    // Liveness: is the process running? Deliberately checks nothing else, so a database blip
    // cannot cause an orchestrator to kill an otherwise healthy instance.
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false,
    });

    // Readiness: can this instance actually serve traffic? Checks dependencies, so a instance
    // that cannot reach SQL Server is removed from the load balancer rather than serving errors.
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
    });

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
