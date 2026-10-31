using ProtonMailMcp.Configuration;

namespace ProtonMailMcp.Credentials;

internal interface IPasswordSource
{
    Task<PasswordLease> GetAsync(CancellationToken cancellationToken);
}

// Deliberately no generated ToString. Strings/third-party copies cannot be zeroized.
internal sealed class PasswordLease(string value) : IDisposable
{
    private string? value = value;
    public string Value => value ?? throw new ObjectDisposedException(nameof(PasswordLease));
    public void Dispose() => value = null;
}

internal static class PasswordSources
{
    internal static IPasswordSource Create(BridgeSettings settings) => settings.PasswordKeyring is { } selector
        ? new LibsecretPasswordSource(selector) : new FileSnapshot(settings.Password);

    private sealed class FileSnapshot(string password) : IPasswordSource
    {
        public Task<PasswordLease> GetAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new PasswordLease(password));
        }
    }
}
