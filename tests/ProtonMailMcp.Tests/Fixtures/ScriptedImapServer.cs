using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ProtonMailMcp.Configuration;

namespace ProtonMailMcp.Tests.Fixtures;

// Synthetic wire fixture. Each session admits only an ordered script; any extra command fails.
internal sealed class ScriptedImapServer : IAsyncDisposable
{
    internal sealed record Step(string Command, string Response = "", bool EndConnection = false,
        bool WaitForDisconnect = false, TaskCompletionSource? Reached = null);
    internal sealed record Session(IReadOnlyList<Step> Steps, bool StartTls = false, bool AdvertiseStartTls = true);
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource lifetime = new(TimeSpan.FromSeconds(60));
    private readonly X509Certificate2 certificate;
    private readonly Task run;
    public List<string> Commands { get; } = [];
    public int AcceptedConnections { get; private set; }
    public bool HasPendingConnection => listener.Pending();
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public byte[] CertificateDer => certificate.RawData;

    public ScriptedImapServer(params Session[] sessions) : this(null, null, sessions) { }
    public ScriptedImapServer(DateTimeOffset? notBefore, DateTimeOffset? notAfter, params Session[] sessions)
    {
        using var key = ECDsa.Create();
        var request = new CertificateRequest("CN=synthetic.invalid", key, HashAlgorithmName.SHA256);
        certificate = request.CreateSelfSigned(notBefore ?? DateTimeOffset.UtcNow.AddDays(-1), notAfter ?? DateTimeOffset.UtcNow.AddDays(1));
        listener.Start();
        run = RunAsync(sessions);
    }

    public BridgeSettings Settings(BridgeTlsMode mode = BridgeTlsMode.SslOnConnect, byte[]? pin = null,
        string[]? folders = null) => new(IPAddress.Loopback, Port, mode,
            SyntheticConfiguration.SentinelUsername, SyntheticConfiguration.SentinelPassword,
            pin ?? CertificateDer, folders ?? ["INBOX"]);

    public static Step[] Login(params Step[] operation) =>
    [
        new("LOGIN SYNTHETIC-username-sentinel SYNTHETIC-password-sentinel", "{tag} OK [CAPABILITY IMAP4rev1] authenticated\r\n"),
        new("LIST \"\" \"\"", "* LIST (\\Noselect) \"/\" \"\"\r\n{tag} OK list\r\n"),
        new("LIST \"\" \"INBOX\"", "* LIST (\\HasNoChildren) \"/\" \"INBOX\"\r\n{tag} OK list\r\n"),
        .. operation
    ];
    public static Step Examine(string access = "READ-ONLY", uint validity = 7) => new("EXAMINE INBOX",
        $"* FLAGS (\\Seen \\Deleted)\r\n* 1 EXISTS\r\n* 0 RECENT\r\n* OK [UIDVALIDITY {validity}] valid\r\n{{tag}} OK [{access}] examined\r\n");
    public static Step Logout() => new("LOGOUT", "* BYE leaving\r\n{tag} OK logout\r\n");

    private async Task RunAsync(Session[] sessions)
    {
        foreach (var session in sessions)
        {
            using var socket = await listener.AcceptTcpClientAsync(lifetime.Token);
            AcceptedConnections++;
            Stream stream = socket.GetStream();
            SslStream? tls = null;
            try
            {
                if (!session.StartTls) stream = tls = await EncryptAsync(stream);
                await SendAsync(stream, "* OK [CAPABILITY IMAP4rev1" +
                    (session.StartTls && session.AdvertiseStartTls ? " STARTTLS" : "") + "] synthetic ready\r\n");
                if (session.StartTls && session.AdvertiseStartTls)
                {
                    var command = await ReadAsync(stream);
                    Assert.EndsWith(" STARTTLS", command);
                    Commands.Add(command[(command.IndexOf(' ') + 1)..]);
                    await SendAsync(stream, command.Split(' ')[0] + " OK upgrade\r\n");
                    stream = tls = await EncryptAsync(stream);
                    var capability = await ReadAsync(stream);
                    Assert.EndsWith(" CAPABILITY", capability);
                    Commands.Add("CAPABILITY");
                    await SendAsync(stream, "* CAPABILITY IMAP4rev1\r\n" + capability.Split(' ')[0] + " OK capability\r\n");
                }
                foreach (var step in session.Steps)
                {
                    var line = await ReadAsync(stream);
                    Assert.NotEmpty(line);
                    var split = line.IndexOf(' ');
                    Assert.True(split > 0, line);
                    var command = line[(split + 1)..];
                    Commands.Add(command);
                    Assert.Matches("\\A(?:" + step.Command + ")\\z", command);
                    step.Reached?.TrySetResult();
                    if (step.EndConnection) { socket.Close(); break; }
                    await SendAsync(stream, step.Response.Replace("{tag}", line[..split]));
                    if (step.WaitForDisconnect) break;
                }
                // Successful logout may close TLS without close_notify; errors dispose the socket.
                var extra = socket.Connected ? await ReadAsync(stream) : "";
                Assert.True(extra.Length == 0, "Unexpected cleanup command: " + extra);
            }
            catch (Exception exception) when (exception is IOException or System.Security.Authentication.AuthenticationException)
            {
                // TLS rejection is expected only for a session with no authenticated script.
                if (session.Steps.Count != 0) throw;
            }
            finally { if (tls is not null) await tls.DisposeAsync(); }
        }
    }

    private async Task<SslStream> EncryptAsync(Stream stream)
    {
        var ssl = new SslStream(stream, leaveInnerStreamOpen: true);
        try
        {
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate }, lifetime.Token);
            return ssl;
        }
        catch { ssl.Dispose(); throw; }
    }
    private async Task SendAsync(Stream stream, string value)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes(value), lifetime.Token);
        await stream.FlushAsync(lifetime.Token);
    }
    private async Task<string> ReadAsync(Stream stream)
    {
        var bytes = new List<byte>();
        var buffer = new byte[1];
        try
        {
            while (await stream.ReadAsync(buffer, lifetime.Token) != 0)
            {
                bytes.Add(buffer[0]);
                if (buffer[0] == '\n') return Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r', '\n');
                Assert.True(bytes.Count < 8192, "Unbounded command");
            }
        }
        catch (IOException) { }
        return Encoding.ASCII.GetString(bytes.ToArray());
    }
    public async Task VerifyAsync()
    {
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(HasPendingConnection, "Unexpected additional connection");
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        listener.Stop();
        try { await run; } catch (OperationCanceledException) { } catch (SocketException) when (lifetime.IsCancellationRequested) { }
        certificate.Dispose();
        lifetime.Dispose();
    }
}
