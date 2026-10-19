using System.Text.Json;
using ProtonMailMcp.Configuration;
using ProtonMailMcp.Tests.Fixtures;

namespace ProtonMailMcp.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class FolderAccessTests
{
    [Theory]
    [InlineData("{\"Mode\":\"Allowlist\",\"AllowedFolders\":[\"inbox\",\"Labels/Work\"]}")]
    [InlineData(null)]
    public async Task ExplicitAndLegacyAllowlistsKeepExactMatching(string? policy)
    {
        using var fixture = new SyntheticConfiguration();
        fixture.Values["AllowedFolders"] = new[] { "inbox", "Labels/Work" };
        if (policy is not null) SetPolicy(fixture, policy);
        fixture.Save();
        var settings = await ConfigurationLoader.LoadAsync(fixture.ConfigPath, CancellationToken.None);
        Assert.Equal(FolderAccessMode.Allowlist, settings.FolderAccessMode);
        Assert.True(settings.IsFolderAllowed("INBOX"));
        Assert.True(settings.IsFolderAllowed("Labels/Work"));
        Assert.False(settings.IsFolderAllowed("Labels/work"));
        Assert.False(settings.IsFolderAllowed("Labels/Work/Child"));
        Assert.False(settings.IsFolderAllowed("NewLabel"));
    }

    [Theory]
    [InlineData("[]", true)]
    [InlineData("[\"inbox\",\"Labels/Private\"]", false)]
    public async Task AllExceptPermitsNewFoldersAndOnlyExcludesExactNames(string exclusions, bool inboxAllowed)
    {
        using var fixture = new SyntheticConfiguration();
        SetPolicy(fixture, "{\"Mode\":\"AllExcept\",\"ExcludedFolders\":" + exclusions + "}");
        fixture.Save();
        var settings = await ConfigurationLoader.LoadAsync(fixture.ConfigPath, CancellationToken.None);
        Assert.Equal(FolderAccessMode.AllExcept, settings.FolderAccessMode);
        Assert.Empty(settings.AllowedFolders);
        Assert.Equal(inboxAllowed, settings.IsFolderAllowed("INBOX"));
        Assert.Equal(inboxAllowed, settings.IsFolderAllowed("Labels/Private"));
        Assert.True(settings.IsFolderAllowed("Labels/private"));
        Assert.True(settings.IsFolderAllowed("Labels/Private/Child"));
        Assert.True(settings.IsFolderAllowed("NewLabel"));
        Assert.True(settings.IsFolderAllowed("All Mail"));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"ExcludedFolders\":[]}")]
    [InlineData("{\"Mode\":null,\"ExcludedFolders\":[]}")]
    [InlineData("{\"Mode\":\"allexcept\",\"ExcludedFolders\":[]}")]
    [InlineData("{\"Mode\":\"All\",\"ExcludedFolders\":[]}")]
    [InlineData("{\"Mode\":1,\"ExcludedFolders\":[]}")]
    [InlineData("{\"Mode\":\"AllExcept\"}")]
    [InlineData("{\"Mode\":\"AllExcept\",\"ExcludedFolders\":null}")]
    [InlineData("{\"Mode\":\"AllExcept\",\"ExcludedFolders\":[null]}")]
    [InlineData("{\"Mode\":\"AllExcept\",\"ExcludedFolders\":[\"\"]}")]
    [InlineData("{\"Mode\":\"AllExcept\",\"ExcludedFolders\":[\"Labels/*\"]}")]
    [InlineData("{\"Mode\":\"AllExcept\",\"ExcludedFolders\":[\"Labels/%\"]}")]
    [InlineData("{\"Mode\":\"AllExcept\",\"ExcludedFolders\":[\"Bad\\nName\"]}")]
    [InlineData("{\"Mode\":\"AllExcept\",\"ExcludedFolders\":[],\"AllowedFolders\":[]}")]
    [InlineData("{\"Mode\":\"AllExcept\",\"ExcludedFolders\":[],\"AllowedFolders\":null}")]
    [InlineData("{\"Mode\":\"AllExcept\",\"ExcludedFolders\":[],\"extra\":true}")]
    [InlineData("{\"mode\":\"AllExcept\",\"ExcludedFolders\":[]}")]
    [InlineData("{\"Mode\":\"Allowlist\"}")]
    [InlineData("{\"Mode\":\"Allowlist\",\"AllowedFolders\":[]}")]
    [InlineData("{\"Mode\":\"Allowlist\",\"AllowedFolders\":null}")]
    [InlineData("{\"Mode\":\"Allowlist\",\"AllowedFolders\":[\"INBOX\"],\"ExcludedFolders\":[]}")]
    [InlineData("{\"Mode\":\"Allowlist\",\"AllowedFolders\":[\"INBOX\"],\"ExcludedFolders\":null}")]
    [InlineData("{\"Mode\":\"Allowlist\",\"Mode\":\"AllExcept\",\"ExcludedFolders\":[]}")]
    public async Task InvalidOrAmbiguousPoliciesFailClosed(string policy)
    {
        using var fixture = new SyntheticConfiguration();
        SetPolicy(fixture, policy);
        fixture.Save();
        await AssertSafeFailure(fixture);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BothConfigurationFormatsAreRejectedEvenWithNullLegacyList(bool nullList)
    {
        using var fixture = new SyntheticConfiguration();
        fixture.Values["FolderAccess"] = new { Mode = "AllExcept", ExcludedFolders = Array.Empty<string>() };
        if (nullList) fixture.Values["AllowedFolders"] = null;
        fixture.Save();
        await AssertSafeFailure(fixture);
    }

    [Theory]
    [InlineData("Allowlist", "AllowedFolders")]
    [InlineData("AllExcept", "ExcludedFolders")]
    public async Task BothListsEnforceNameAndEntryLimits(string mode, string key)
    {
        using var fixture = new SyntheticConfiguration();
        fixture.Values.Remove("AllowedFolders");
        var policy = new Dictionary<string, object> { ["Mode"] = mode, [key] = new[] { new string('x', 1025) } };
        fixture.Values["FolderAccess"] = policy;
        fixture.Save();
        await AssertSafeFailure(fixture);
        policy[key] = Enumerable.Range(0, 257).Select(i => "Label" + i).ToArray();
        fixture.Save();
        await AssertSafeFailure(fixture);
        policy[key] = Enumerable.Range(0, 256).Select(i => "Label" + i).ToArray();
        fixture.Save();
        await ConfigurationLoader.LoadAsync(fixture.ConfigPath, CancellationToken.None);
    }

    private static void SetPolicy(SyntheticConfiguration fixture, string json)
    {
        fixture.Values.Remove("AllowedFolders");
        fixture.Values["FolderAccess"] = JsonSerializer.Deserialize<JsonElement>(json);
    }

    private static async Task AssertSafeFailure(SyntheticConfiguration fixture)
    {
        var error = await Assert.ThrowsAsync<ConfigurationException>(() =>
            ConfigurationLoader.LoadAsync(fixture.ConfigPath, CancellationToken.None));
        Assert.Matches("^configuration_[a-z_]+$", error.Message);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(fixture.DirectoryPath, error.ToString());
        Assert.DoesNotContain(SyntheticConfiguration.SentinelUsername, error.ToString());
        Assert.DoesNotContain(SyntheticConfiguration.SentinelPassword, error.ToString());
        Assert.DoesNotContain("Labels", error.ToString());
    }
}
