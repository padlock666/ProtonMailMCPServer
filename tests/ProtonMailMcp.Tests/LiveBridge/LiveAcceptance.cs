using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using ProtonMailMcp.Configuration;
using ProtonMailMcp.Contracts;
using ProtonMailMcp.Mail;

namespace ProtonMailMcp.Tests.LiveBridge;

// No discovery-time file reads or network access. Opt-in is evaluated before any local settings load.
internal sealed class LiveBridgeFactAttribute : FactAttribute
{
    public LiveBridgeFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PROTONMCP_LIVE") != "I_AUTHORIZE_READ_ONLY_BRIDGE_TESTS")
            Skip = "Live Bridge access requires explicit operator authorization.";
    }
}

internal sealed class LiveManifest
{
    public string Folder { get; init; } = "";
    public uint UidValidity { get; init; }
    public LiveFixture[] Messages { get; init; } = [];
    public string DeletedState { get; init; } = "";
    public uint PlainUid { get; init; }
    public uint HtmlUid { get; init; }
    public uint AttachmentUid { get; init; }
    public string PlainTextSha256 { get; init; } = "";
    public string HtmlTextSha256 { get; init; } = "";

    internal void Validate(BridgeSettings settings)
    {
        Require(settings.AllowedFolders.Count == 1 && settings.IsFolderAllowed(Folder) &&
            !string.Equals(Folder, "INBOX", StringComparison.OrdinalIgnoreCase), "dedicated_folder_required");
        Require(UidValidity != 0 && Messages.Length is >= 2 and <= 50 &&
            Messages.All(m => m.Uid != 0 && m.Keywords is not null &&
                (m.Flags & ~(int)(MessageFlags.Seen | MessageFlags.Answered | MessageFlags.Flagged |
                    MessageFlags.Deleted | MessageFlags.Draft)) == 0) &&
            Messages.Select(m => m.Uid).Distinct().Count() == Messages.Length, "fixture_identities_invalid");
        Require(new[] { PlainTextSha256, HtmlTextSha256 }.All(h => h.Length == 64 && h.All(char.IsAsciiHexDigit)), "expected_text_hashes_required");
        Require(PlainUid != HtmlUid && new[] { PlainUid, HtmlUid, AttachmentUid }.All(uid => Messages.Any(m => m.Uid == uid)),
            "content_fixtures_required");
        Require(Messages.Any(m => (m.Flags & (int)MessageFlags.Seen) == 0) &&
            Messages.Any(m => (m.Flags & (int)MessageFlags.Seen) != 0), "read_unread_fixtures_required");
        Require(DeletedState == "present" && Messages.Any(m => (m.Flags & (int)MessageFlags.Deleted) != 0) ||
            DeletedState == "unsupported" && Messages.All(m => (m.Flags & (int)MessageFlags.Deleted) == 0),
            "deleted_state_invalid");
    }

    internal static void Require(bool condition, string code)
    {
        // Never pass live values to xUnit assertions, exception text, or test output.
        if (!condition) throw new InvalidOperationException("live: " + code);
    }
}

internal sealed class LiveFixture
{
    public uint Uid { get; init; }
    public int Flags { get; init; }
    public string[] Keywords { get; init; } = [];
}

[Trait("Category", "LiveBridge")]
public sealed class LiveAcceptance
{
    [LiveBridgeFact]
    public async Task OperatorPreparedFolderPreservesState()
    {
        try
        {
            LiveManifest.Require(Environment.GetEnvironmentVariable("PROTONMCP_LIVE") ==
                "I_AUTHORIZE_READ_ONLY_BRIDGE_TESTS", "authorization_required");
            var stage = Environment.GetEnvironmentVariable("PROTONMCP_LIVE_STAGE");
            LiveManifest.Require(stage is "reads" or "unavailable" or "recovered", "stage_required");
            var configPath = ExternalPath("PROTONMCP_LIVE_CONFIG");
            var manifestPath = ExternalPath("PROTONMCP_LIVE_FIXTURES");
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var token = deadline.Token;
            // Validate referenced paths before the production loader reads any credential material.
            await using (var configFile = File.OpenRead(configPath))
            {
                LiveManifest.Require(configFile.Length <= 65536, "configuration_too_large");
                var config = await JsonSerializer.DeserializeAsync<ConfigurationDocument>(configFile, cancellationToken: token);
                ExternalValue(config?.PasswordFile);
                ExternalValue(config?.CertificateFile);
            }
            var settings = await ConfigurationLoader.LoadAsync(configPath, token);
            await using var file = File.OpenRead(manifestPath);
            LiveManifest.Require(file.Length <= 65536, "manifest_too_large");
            var manifest = await JsonSerializer.DeserializeAsync<LiveManifest>(file,
                new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }, token);
            LiveManifest.Require(manifest is not null, "manifest_required");
            manifest!.Validate(settings);
            using var mailbox = new MailKitMailbox(settings);
            if (stage == "unavailable")
            {
                await ExpectError(() => mailbox.SearchAsync(new(manifest.Folder), token), MailboxError.BridgeUnavailable);
                return;
            }

            await VerifyState(settings, manifest, token);
            for (var repeat = 0; repeat < 2; repeat++)
            {
                var folders = await mailbox.ListFoldersAsync(token);
                LiveManifest.Require(folders.Count == 1 && settings.IsFolderAllowed(folders[0].Name), "folder_listing_failed");
                foreach (bool? unread in new bool?[] { null, true, false })
                {
                    var found = await mailbox.SearchAsync(new(manifest.Folder, Unread: unread, Limit: 50), token);
                    var expected = manifest.Messages.Where(m => unread is null ||
                        ((m.Flags & (int)MessageFlags.Seen) == 0) == unread).Select(m => m.Uid).OrderDescending();
                    LiveManifest.Require(!found.Truncated && found.Messages.Select(m => m.Reference.Uid).SequenceEqual(expected) &&
                        found.Messages.All(m => m.Reference.UidValidity == manifest.UidValidity), "search_mismatch");
                }
                foreach (var fixture in manifest.Messages)
                {
                    var result = await mailbox.GetMessageAsync(new(manifest.Folder, manifest.UidValidity, fixture.Uid), token);
                    LiveManifest.Require(result.Summary.Seen == ((fixture.Flags & (int)MessageFlags.Seen) != 0), "seen_changed");
                    if (fixture.Uid == manifest.PlainUid || fixture.Uid == manifest.HtmlUid)
                        LiveManifest.Require(result.ContentStatus == ContentStatus.Available &&
                            !string.IsNullOrEmpty(result.Text) && !result.Truncated, "text_fixture_failed");
                    if (fixture.Uid == manifest.PlainUid || fixture.Uid == manifest.HtmlUid)
                    {
                        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(result.Text!)));
                        LiveManifest.Require(string.Equals(hash, fixture.Uid == manifest.PlainUid ?
                            manifest.PlainTextSha256 : manifest.HtmlTextSha256, StringComparison.OrdinalIgnoreCase), "text_conversion_mismatch");
                    }
                    if (fixture.Uid == manifest.AttachmentUid)
                        LiveManifest.Require(result.Attachments.Count > 0 && !result.AttachmentsTruncated, "attachment_fixture_failed");
                }
            }
            await VerifyState(settings, manifest, token);
            if (stage == "reads")
            {
                // Isolated in-memory settings; never change files or learn a replacement pin.
                using var key = ECDsa.Create();
                using var wrongCert = new CertificateRequest("CN=synthetic.invalid", key, HashAlgorithmName.SHA256)
                    .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
                using var wrongPin = new MailKitMailbox(Copy(settings, settings.Password, wrongCert.RawData));
                await ExpectError(() => wrongPin.SearchAsync(new(manifest.Folder), token), MailboxError.TlsFailed);
                using var wrongPassword = new MailKitMailbox(Copy(settings, "SYNTHETIC-wrong-" + Guid.NewGuid().ToString("N"), settings.CertificateDer.ToArray()));
                await ExpectError(() => wrongPassword.SearchAsync(new(manifest.Folder), token), MailboxError.AuthenticationFailed);
                await mailbox.SearchAsync(new(manifest.Folder), token);
                await VerifyState(settings, manifest, token);
            }
        }
        catch
        {
            // Includes raw MailKit/JSON/filesystem failures from the test-only observer: no inner exceptions.
            throw new InvalidOperationException("Live acceptance failed; inspect setup locally. No live details are reported.");
        }
    }

    private static BridgeSettings Copy(BridgeSettings s, string password, byte[] pin) =>
        new(s.Host, s.Port, s.TlsMode, s.Username, password, pin, s.AllowedFolders);

    internal static string RequireExternalPath(string? path, string repository)
    {
        LiveManifest.Require(!string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path), "external_path_required");
        var full = Path.GetFullPath(path!);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repository));
        LiveManifest.Require(full != root && !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal), "external_path_required");
        // Resolve each existing component; lexical checks alone permit symlink escapes into the checkout.
        var current = Path.GetPathRoot(full)!;
        foreach (var component in full[ current.Length.. ].Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, component);
            var info = new FileInfo(current);
            if (info.LinkTarget is not null) current = info.ResolveLinkTarget(true)!.FullName;
            LiveManifest.Require(current != root && !current.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal), "external_path_required");
        }
        return full;
    }

    private static void ExternalValue(string? path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProtonMailMcp.slnx"))) directory = directory.Parent;
        LiveManifest.Require(directory is not null, "repository_root_unknown");
        RequireExternalPath(path, directory!.FullName);
    }

    private static string ExternalPath(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProtonMailMcp.slnx"))) directory = directory.Parent;
        LiveManifest.Require(directory is not null, "repository_root_unknown");
        return RequireExternalPath(Environment.GetEnvironmentVariable(name), directory!.FullName);
    }

    private static async Task ExpectError(Func<Task<SearchResult>> call, MailboxError expected)
    {
        try { await call(); }
        catch (MailboxException ex) when (ex.Error == expected) { return; }
        throw new InvalidOperationException("live: expected_failure_missing");
    }

    // Test-only observer: UID/FLAGS only, no envelopes, bodies, protocol logging, or folder close.
    internal static async Task VerifyState(BridgeSettings settings, LiveManifest manifest, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        token = deadline.Token;
        using var client = new ImapClient();
        client.ServerCertificateValidationCallback = (_, cert, _, _) =>
        {
            if (cert is null) return false;
            using var peer = new X509Certificate2(cert);
            return DateTime.UtcNow >= peer.NotBefore.ToUniversalTime() && DateTime.UtcNow <= peer.NotAfter.ToUniversalTime() &&
                CryptographicOperations.FixedTimeEquals(peer.RawData, settings.CertificateDer.Span);
        };
        await client.ConnectAsync(settings.Host.ToString(), settings.Port,
            settings.TlsMode == BridgeTlsMode.StartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect, token);
        LiveManifest.Require(client.IsSecure && settings.IsFolderAllowed(manifest.Folder), "observer_policy_failed");
        await client.AuthenticateAsync(settings.Username, settings.Password, token);
        var folder = await client.GetFolderAsync(manifest.Folder, token);
        LiveManifest.Require(settings.IsFolderAllowed(folder.FullName), "observer_policy_failed");
        await folder.OpenAsync(FolderAccess.ReadOnly, token);
        LiveManifest.Require(folder.Access == FolderAccess.ReadOnly && folder.UidValidity == manifest.UidValidity &&
            folder.Count == manifest.Messages.Length, "folder_state_changed");
        var uids = await folder.SearchAsync(SearchQuery.All, token);
        LiveManifest.Require(uids.Select(u => u.Id).Order().SequenceEqual(manifest.Messages.Select(m => m.Uid).Order()), "membership_changed");
        var flags = await folder.FetchAsync(uids, MessageSummaryItems.UniqueId | MessageSummaryItems.Flags, token);
        LiveManifest.Require(flags.Count == manifest.Messages.Length, "flags_missing");
        foreach (var expected in manifest.Messages)
        {
            var actual = flags.SingleOrDefault(m => m.UniqueId.Id == expected.Uid);
            LiveManifest.Require(actual?.Flags is not null &&
                (actual.Flags.Value & ~MessageFlags.Recent & ~MessageFlags.UserDefined) == (MessageFlags)expected.Flags &&
                (actual.Keywords ?? new HashSet<string>()).Order(StringComparer.Ordinal).SequenceEqual(expected.Keywords.Order(StringComparer.Ordinal)), "persistent_flags_changed");
        }
        await client.DisconnectAsync(true, token);
        token.ThrowIfCancellationRequested();
    }
}
