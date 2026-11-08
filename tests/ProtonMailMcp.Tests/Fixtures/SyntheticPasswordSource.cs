using ProtonMailMcp.Credentials;

namespace ProtonMailMcp.Tests.Fixtures;

internal sealed class SyntheticPasswordSource(string value = SyntheticConfiguration.SentinelPassword) : IPasswordSource
{
    public Task<PasswordLease> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PasswordLease(value));
    }
}
