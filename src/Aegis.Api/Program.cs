using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseHttpsRedirection();
app.MapControllers();

// Liveness probe. Deliberately dependency-free: it answers "is the process up?", which is a
// different question from "can this instance serve traffic?". Readiness — SQL Server reachable,
// Redis reachable, migrations applied — arrives with the Infrastructure wiring in the next
// increment. Conflating the two causes orchestrators to kill healthy pods during a database blip.
app.MapGet("/health/live", () => Results.Ok(new { status = "healthy", service = "Aegis.Api" }))
   .WithName("Liveness")
   .ExcludeFromDescription();

await app.RunAsync();

/// <summary>
/// Exposed so that <c>WebApplicationFactory&lt;Program&gt;</c> in the integration test project can
/// locate the entry point; top-level statements otherwise compile to an internal class.
/// </summary>
[ApiExplorerSettings(IgnoreApi = true)]
public partial class Program;
