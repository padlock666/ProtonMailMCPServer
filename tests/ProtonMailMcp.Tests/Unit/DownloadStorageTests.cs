using System.Text;
using ProtonMailMcp.Configuration;
using ProtonMailMcp.Contracts;
using ProtonMailMcp.Mail;
using ProtonMailMcp.Tests.Fixtures;
using MailKit;
using MimeKit;

namespace ProtonMailMcp.Tests.Unit;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
[Trait("Category", "Unit")]
public sealed class DownloadStorageTests
{
    [Fact]
    public async Task OptionalConfigurationAndOperationLockAreExplicit()
    {
        using var config = new SyntheticConfiguration();
        Assert.Null((await ConfigurationLoader.LoadAsync(config.ConfigPath, default)).DownloadDirectory);
        var path = config.EnableDownloads();
        Assert.Equal(path, (await ConfigurationLoader.LoadAsync(config.ConfigPath, default)).DownloadDirectory);
        var first = new PrivateDownloadStore(path);
        var second = new PrivateDownloadStore(path);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var held = await first.AcquireAsync(timeout.Token);
        try
        {
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            var waiting = second.AcquireAsync(cancel.Token);
            Assert.False(waiting.IsCompleted);
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
            held.Check(true, default);
        }
        finally { held.Dispose(); }
        using var acquired = await second.AcquireAsync(timeout.Token);
        acquired.Check(true, default);
        Assert.Empty(Directory.GetFiles(path));
    }

    [Fact]
    public async Task WaitingDownloadRechecksQuotaAfterThePreviousOperation()
    {
        using var config = new SyntheticConfiguration();
        var path = config.EnableDownloads();
        var held = await new PrivateDownloadStore(path).AcquireAsync(default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var waiting = new PrivateDownloadStore(path).AcquireAsync(timeout.Token);
        try
        {
            Assert.False(waiting.IsCompleted);
            var file = Path.Combine(path, "synthetic-quota");
            using (var stream = File.Create(file)) stream.SetLength(PrivateDownloadStore.MaxDirectoryBytes);
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        finally { held.Dispose(); }
        Assert.Equal(MailboxError.DownloadLimitExceeded,
            (await Assert.ThrowsAsync<MailboxException>(() => waiting)).Error);
        File.Delete(Path.Combine(path, "synthetic-quota"));
        using var recovered = await new PrivateDownloadStore(path).AcquireAsync(timeout.Token);
    }

    [Fact]
    public async Task WaitingDownloadRejectsDirectoryReplacementAndReleasesTheOldLock()
    {
        using var config = new SyntheticConfiguration();
        var path = config.EnableDownloads();
        var held = await new PrivateDownloadStore(path).AcquireAsync(default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var waiting = new PrivateDownloadStore(path).AcquireAsync(timeout.Token);
        try
        {
            Assert.False(waiting.IsCompleted);
            Directory.Move(path, path + ".old");
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        finally { held.Dispose(); }
        Assert.Equal(MailboxError.DownloadDirectoryUnsafe,
            (await Assert.ThrowsAsync<MailboxException>(() => waiting)).Error);
        using var recovered = await new PrivateDownloadStore(path + ".old").AcquireAsync(timeout.Token);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"Directory\":null}")]
    [InlineData("{\"Directory\":\"relative\"}")]
    [InlineData("{\"Directory\":\"/tmp/../other\"}")]
    [InlineData("{\"Directory\":\"/tmp/\"}")]
    [InlineData("{\"Directory\":\"/\"}")]
    [InlineData("{\"Directory\":\"/tmp\",\"MaxFileBytes\":1}")]
    public async Task InvalidSettingsFailWithoutSensitiveText(string json)
    {
        using var config = new SyntheticConfiguration();
        config.Values["AttachmentDownloads"] = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
        config.Save();
        var error = await Assert.ThrowsAsync<ConfigurationException>(() => ConfigurationLoader.LoadAsync(config.ConfigPath, default));
        Assert.Matches("^configuration_[a-z_]+$", error.Message);
        Assert.DoesNotContain(config.DirectoryPath, error.ToString());
    }

    [Fact]
    public async Task RejectsCredentialDirectoryAndSymlinkedOrReplacedRoots()
    {
        using var config = new SyntheticConfiguration();
        config.Values["AttachmentDownloads"] = new { Directory = config.DirectoryPath };
        config.Save();
        await Assert.ThrowsAsync<ConfigurationException>(() => ConfigurationLoader.LoadAsync(config.ConfigPath, default));
        var path = config.EnableDownloads();
        using var store = await new PrivateDownloadStore(path).AcquireAsync(default);
        Directory.Move(path, path + ".old");
        Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Assert.Equal(MailboxError.DownloadDirectoryUnsafe, Assert.Throws<MailboxException>(() => store.Check(true, default)).Error);
        Directory.Delete(path);
        Directory.CreateSymbolicLink(path, path + ".old");
        await Assert.ThrowsAsync<MailboxException>(() => new PrivateDownloadStore(path).AcquireAsync(default));
        Directory.Delete(path);
    }

    [Theory]
    [InlineData("symlink")]
    [InlineData("directory")]
    [InlineData("public")]
    [InlineData("executable")]
    public async Task RefusesUnsafeEntriesWithoutFollowingOrDeletingThem(string kind)
    {
        using var config = new SyntheticConfiguration();
        var path = config.EnableDownloads();
        using var store = await new PrivateDownloadStore(path).AcquireAsync(default);
        var entry = Path.Combine(path, "foreign");
        if (kind == "symlink") File.CreateSymbolicLink(entry, config.FixturePath);
        else if (kind == "directory") Directory.CreateDirectory(entry);
        else
        {
            File.WriteAllText(entry, "synthetic");
            File.SetUnixFileMode(entry, kind == "public" ? UnixFileMode.UserRead | UnixFileMode.GroupRead :
                UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }
        Assert.Equal(MailboxError.DownloadDirectoryUnsafe, Assert.Throws<MailboxException>(() => store.Check(true, default)).Error);
        Assert.True(Path.Exists(entry));
    }

    [Fact]
    public async Task QuotaCountsSparseFilesAndCrashLeftoversAndRecoversAfterOperatorRemoval()
    {
        using var config = new SyntheticConfiguration();
        var path = config.EnableDownloads();
        using var store = await new PrivateDownloadStore(path).AcquireAsync(default);
        var file = Path.Combine(path, ".abandoned.part");
        using (var stream = File.Create(file)) stream.SetLength(PrivateDownloadStore.MaxDirectoryBytes - PrivateDownloadStore.MaxFileBytes);
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        store.Check(true, default);
        using (var stream = File.OpenWrite(file)) stream.SetLength(PrivateDownloadStore.MaxDirectoryBytes - PrivateDownloadStore.MaxFileBytes + 1);
        Assert.Equal(MailboxError.DownloadLimitExceeded, Assert.Throws<MailboxException>(() => store.Check(true, default)).Error);
        File.Delete(file);
        for (var i = 0; i < PrivateDownloadStore.MaxEntries; i++)
        {
            var name = Path.Combine(path, i.ToString());
            File.WriteAllBytes(name, []);
            File.SetUnixFileMode(name, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        store.Check(false, default);
        Assert.Equal(MailboxError.DownloadLimitExceeded, Assert.Throws<MailboxException>(() => store.Check(true, default)).Error);
        File.Delete(Path.Combine(path, "0"));
        store.Check(true, default);
    }

    [Fact]
    public async Task AtomicFilesArePrivateDistinctAndRetainedButIncompleteFilesAreRemoved()
    {
        using var config = new SyntheticConfiguration();
        var path = config.EnableDownloads();
        using var store = await new PrivateDownloadStore(path).AcquireAsync(default);
        string Save()
        {
            var temp = store.Create();
            temp.Stream.Write(Encoding.ASCII.GetBytes("synthetic PDF"));
            temp.Stream.Dispose();
            var final = temp.Publish(".pdf", default);
            temp.Cleanup();
            return final;
        }
        var first = Save();
        var second = Save();
        Assert.NotEqual(first, second);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(first));
        Assert.Matches("^attachment-[a-f0-9]{32}\\.pdf$", Path.GetFileName(first));
        Assert.Equal("synthetic PDF", await File.ReadAllTextAsync(first));
        var incomplete = store.Create();
        incomplete.Cleanup();
        Assert.Equal(2, Directory.GetFiles(path).Length);
    }

    [Theory]
    [InlineData("base64", "AAH/", new byte[] { 0, 1, 255 })]
    [InlineData("base64", "YQ==\r\n", new byte[] { 97 })]
    [InlineData("quoted-printable", "=00=FFa=\r\nb", new byte[] { 0, 255, 97, 98 })]
    [InlineData("7bit", "hello\r\n", new byte[] { 104, 101, 108, 108, 111, 13, 10 })]
    [InlineData("binary", "", new byte[] { })]
    public async Task StrictDecoderPreservesBytes(string encoding, string encoded, byte[] expected)
    {
        using var source = new MemoryStream(Encoding.ASCII.GetBytes(encoded));
        using var output = new MemoryStream();
        var result = await AttachmentParts.DecodeAsync(source, output, encoding, default);
        Assert.Equal(expected, output.ToArray());
        Assert.Equal(expected.Length, result.Length);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(expected)), result.Hash);
    }

    [Theory]
    [InlineData("base64", "YQ=")]
    [InlineData("base64", "YR==")]
    [InlineData("base64", "YQ==YQ==")]
    [InlineData("base64", "@@@@")]
    [InlineData("quoted-printable", "=Q0")]
    [InlineData("quoted-printable", "=0")]
    [InlineData("quoted-printable", "=\n")]
    public async Task MalformedBytesAreNeverRepaired(string encoding, string input)
    {
        using var source = new MemoryStream(Encoding.ASCII.GetBytes(input));
        using var output = new MemoryStream();
        Assert.Equal(MailboxError.AttachmentMalformed, (await Assert.ThrowsAsync<MailboxException>(() =>
            AttachmentParts.DecodeAsync(source, output, encoding, default))).Error);
    }

    [Fact]
    public async Task DecoderEnforcesExactBoundaryWithoutFullDecodedAllocation()
    {
        using var source = new MemoryStream(new byte[PrivateDownloadStore.MaxFileBytes]);
        Assert.Equal(PrivateDownloadStore.MaxFileBytes, (await AttachmentParts.DecodeAsync(source, Stream.Null, "binary", default)).Length);
        using var oversized = new MemoryStream(new byte[PrivateDownloadStore.MaxFileBytes + 1]);
        Assert.Equal(MailboxError.AttachmentTooLarge, (await Assert.ThrowsAsync<MailboxException>(() =>
            AttachmentParts.DecodeAsync(oversized, Stream.Null, "binary", default))).Error);
    }

    [Fact]
    public async Task PublicationNeverOverwritesAnExistingDestinationAndCleanupKeepsIt()
    {
        using var config = new SyntheticConfiguration();
        var path = config.EnableDownloads();
        using var store = await (new PrivateDownloadStore(path) { NameFactory = () => new string('a', 32) }).AcquireAsync(default);
        var final = Path.Combine(path, "attachment-" + new string('a', 32) + ".pdf");
        File.WriteAllText(final, "operator-owned sentinel");
        File.SetUnixFileMode(final, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var temp = store.Create();
        temp.Stream.Dispose();
        Assert.Equal(MailboxError.DownloadStorageFailed, Assert.Throws<MailboxException>(() => temp.Publish(".pdf", default)).Error);
        temp.Cleanup();
        Assert.Equal("operator-owned sentinel", File.ReadAllText(final));
        Assert.Single(Directory.GetFiles(path));
    }

    [Fact]
    public async Task ChangedTemporaryIdentityIsNeverPublishedOrDeleted()
    {
        using var config = new SyntheticConfiguration();
        var path = config.EnableDownloads();
        using var store = await new PrivateDownloadStore(path).AcquireAsync(default);
        var temp = store.Create();
        temp.Stream.Dispose();
        var name = Assert.Single(Directory.GetFiles(path));
        File.Move(name, name + ".old");
        File.WriteAllText(name, "replacement sentinel");
        File.SetUnixFileMode(name, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Assert.Equal(MailboxError.DownloadDirectoryUnsafe, Assert.Throws<MailboxException>(() => temp.Publish(".pdf", default)).Error);
        Assert.Equal(MailboxError.DownloadCleanupFailed, Assert.Throws<MailboxException>(() => temp.Cleanup()).Error);
        Assert.Equal("replacement sentinel", File.ReadAllText(name));
    }

    [Fact]
    public async Task HardLinksAndRootPermissionChangesFailClosed()
    {
        using var config = new SyntheticConfiguration();
        var path = config.EnableDownloads();
        using var store = await new PrivateDownloadStore(path).AcquireAsync(default);
        var info = new System.Diagnostics.ProcessStartInfo("ln") { UseShellExecute = false };
        info.ArgumentList.Add(config.FixturePath);
        info.ArgumentList.Add(Path.Combine(path, "linked"));
        using var process = System.Diagnostics.Process.Start(info)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        Assert.Equal(MailboxError.DownloadDirectoryUnsafe, Assert.Throws<MailboxException>(() => store.Check(true, default)).Error);
        File.Delete(Path.Combine(path, "linked"));
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupExecute);
        Assert.Equal(MailboxError.DownloadDirectoryUnsafe, Assert.Throws<MailboxException>(() => store.Check(true, default)).Error);
    }

    [Fact]
    public void AttachmentMappingNeverDescendsIntoMessagesOrAttachedContainersAndRejectsAmbiguity()
    {
        static BodyPartBasic Pdf(string id) => new(new ContentType("application", "pdf"), id)
        { ContentDisposition = new ContentDisposition("attachment") };
        var root = new BodyPartMultipart(new ContentType("multipart", "mixed"), "");
        root.BodyParts.Add(Pdf("1"));
        root.BodyParts.Add(new BodyPartMessage(new ContentType("message", "rfc822"), "2")
        { Body = Pdf("2.1"), ContentDisposition = new ContentDisposition("attachment") });
        var container = new BodyPartMultipart(new ContentType("multipart", "mixed"), "3")
        { ContentDisposition = new ContentDisposition("attachment") };
        container.BodyParts.Add(Pdf("3.1"));
        root.BodyParts.Add(container);
        Assert.Equal(new[] { "1", "2", "3" }, AttachmentParts.Map(root, default).Keys.Order());
        root.BodyParts.Add(Pdf("1"));
        Assert.Equal(MailboxError.ProtocolFailure, Assert.Throws<MailboxException>(() => AttachmentParts.Map(root, default)).Error);
    }

    [Fact]
    public void AttachmentMappingBoundsDepthAndNodeCount()
    {
        var root = new BodyPartMultipart(new ContentType("multipart", "mixed"), "");
        for (var i = 0; i < 1024; i++) root.BodyParts.Add(new BodyPartBasic(new ContentType("image", "png"), (i + 1).ToString()));
        Assert.Equal(MailboxError.AttachmentUnsupported, Assert.Throws<MailboxException>(() => AttachmentParts.Map(root, default)).Error);
        var current = root = new BodyPartMultipart(new ContentType("multipart", "mixed"), "");
        for (var i = 0; i < 17; i++)
        {
            var next = new BodyPartMultipart(new ContentType("multipart", "mixed"), "1");
            current.BodyParts.Add(next);
            current = next;
        }
        Assert.Equal(MailboxError.AttachmentUnsupported, Assert.Throws<MailboxException>(() => AttachmentParts.Map(root, default)).Error);
    }
}
