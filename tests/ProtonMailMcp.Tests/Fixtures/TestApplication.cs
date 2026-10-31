namespace ProtonMailMcp.Tests.Fixtures;

internal static class TestApplication
{
    // Test-runner setting only; the production application still accepts only --config.
    internal static string Path => Environment.GetEnvironmentVariable("PROTONMCP_TEST_APPLICATION") is { Length: > 0 } path
        ? System.IO.Path.GetFullPath(path)
        : System.IO.Path.Combine(AppContext.BaseDirectory, "ProtonMailMcp.dll");
}
