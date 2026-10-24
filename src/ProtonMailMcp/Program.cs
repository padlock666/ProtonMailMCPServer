using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ProtonMailMcp.Configuration;
using ProtonMailMcp.Mail;
using ProtonMailMcp.Tools;

try
{
    if (args is not ["--config", var path])
        throw new ConfigurationException("configuration_usage_requires_config_path");
    var settings = await ConfigurationLoader.LoadAsync(path, CancellationToken.None);
    using var downloads = settings.DownloadDirectory is { } directory ? new PrivateDownloadStore(directory) : null;
    // Do not ingest appsettings, environment overrides, or command-line provider settings.
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        DisableDefaults = true,
        Args = []
    });
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options =>
    {
        options.LogToStandardErrorThreshold = LogLevel.Trace;
    });
    // Framework/SDK diagnostics may include protocol data or exceptions; disable them.
    // Application diagnostics are fixed codes written directly to stderr below.
    builder.Logging.AddFilter(_ => false);
    builder.Services.AddSingleton(settings);
    builder.Services.AddSingleton<SearchCursorCodec>();
    if (downloads is not null) builder.Services.AddSingleton(downloads);
    builder.Services.AddSingleton<IReadOnlyMailbox, MailKitMailbox>();
    builder.Services.AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation { Name = "ProtonMailMcp", Version = "0.1.0" };
    }).WithStdioServerTransport().WithTools(MailboxTool.CreateApproved(downloads is not null));
    using var host = builder.Build();
    await host.RunAsync();
    return 0;
}
catch (ConfigurationException exception)
{
    await Console.Error.WriteLineAsync($"startup: {exception.Message}");
    return 2;
}
catch (OperationCanceledException)
{
    return 0;
}
catch (Exception)
{
    await Console.Error.WriteLineAsync("host: failed");
    return 1;
}
