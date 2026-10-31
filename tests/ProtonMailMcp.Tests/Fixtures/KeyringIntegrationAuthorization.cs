namespace ProtonMailMcp.Tests.Fixtures;

internal static class KeyringIntegrationAuthorization
{
    internal const string Gate = "PROTONMCP_KEYRING_INTEGRATION";
    internal const string Value = "I_AUTHORIZE_ISOLATED_SYNTHETIC_KEYRING_TESTS";
}

internal sealed class KeyringIntegrationFactAttribute : FactAttribute
{
    public KeyringIntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(KeyringIntegrationAuthorization.Gate) != KeyringIntegrationAuthorization.Value)
            Skip = "Requires explicit isolated synthetic GNOME Keyring test opt-in.";
    }
}
