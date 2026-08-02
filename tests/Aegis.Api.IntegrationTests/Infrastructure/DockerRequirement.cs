using System.Runtime.InteropServices;

namespace Aegis.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Detects whether a Docker daemon is reachable, so container-backed tests can skip on a machine
/// that has none instead of failing.
/// </summary>
/// <remarks>
/// <para>
/// A developer without Docker running should get a clear skip, not a wall of connection errors
/// that buries the unit test results they were actually looking at.
/// </para>
/// <para>
/// <b>Skipping must never be silent in CI.</b> A suite that quietly skips its integration tests
/// reports green while proving nothing, which is worse than no suite at all — it produces
/// confidence without evidence. Setting <c>AEGIS_REQUIRE_DOCKER=1</c> disables skipping entirely,
/// so a CI runner with a broken daemon fails loudly. The CI workflow sets it.
/// </para>
/// </remarks>
public static class DockerRequirement
{
    private const string RequireVariable = "AEGIS_REQUIRE_DOCKER";

    private static readonly Lazy<bool> Available = new(Detect, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>True when a Docker daemon endpoint is present.</summary>
    public static bool IsAvailable => Available.Value;

    /// <summary>True when the environment forbids skipping container-backed tests.</summary>
    public static bool IsRequired =>
        Environment.GetEnvironmentVariable(RequireVariable) is "1" or "true" or "True";

    /// <summary>The skip reason, or null when the tests should run.</summary>
    public static string? SkipReason =>
        IsAvailable || IsRequired
            ? null
            : "Docker is not reachable. Start Docker Desktop to run the container-backed " +
              "integration suite, or set AEGIS_REQUIRE_DOCKER=1 to fail instead of skipping.";

    private static bool Detect()
    {
        // An explicit DOCKER_HOST wins: it is how remote and rootless daemons are addressed, and
        // neither exposes the default local endpoint.
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_HOST")))
        {
            return true;
        }

        // Checking for the endpoint rather than shelling out to `docker info`: this runs during
        // test discovery, and spawning a process there costs a visible pause in every IDE.
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Directory.Exists(@"\\.\pipe\") && File.Exists(@"\\.\pipe\docker_engine")
            : File.Exists("/var/run/docker.sock");
    }
}

/// <summary>A <see cref="FactAttribute"/> that skips when Docker is unavailable.</summary>
public sealed class DockerFactAttribute : FactAttribute
{
    /// <summary>Initialises the attribute, applying the skip reason when appropriate.</summary>
    public DockerFactAttribute() => Skip = DockerRequirement.SkipReason;
}

/// <summary>A <see cref="TheoryAttribute"/> that skips when Docker is unavailable.</summary>
public sealed class DockerTheoryAttribute : TheoryAttribute
{
    /// <summary>Initialises the attribute, applying the skip reason when appropriate.</summary>
    public DockerTheoryAttribute() => Skip = DockerRequirement.SkipReason;
}
