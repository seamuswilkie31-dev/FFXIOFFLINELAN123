using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace LocalXI.Core.Network;

/// <summary>Result codes from LandSandBoat's src/login/auth_session.h.</summary>
public enum LoginResult : byte
{
    Fail = 0x00,
    Success = 0x01,
    Error = 0x02,
    SuccessCreate = 0x03,
    ErrorCreateTaken = 0x04,
    ErrorCreateDisabled = 0x08,
    ErrorCreate = 0x09,
    ErrorAlreadyLoggedIn = 0x0A,
    ErrorVersionUnsupported = 0x0B,
}

public sealed record LoginReply(LoginResult? Result, string? ErrorMessage)
{
    public string Describe() => ErrorMessage ?? Result switch
    {
        LoginResult.SuccessCreate => "Account created.",
        LoginResult.ErrorCreateTaken => "That username is already taken on this server.",
        LoginResult.ErrorCreateDisabled => "This server does not allow creating accounts from the launcher. Ask the host to create one for you.",
        LoginResult.ErrorCreate => "The server could not create the account.",
        LoginResult.ErrorVersionUnsupported => "The server needs a different xiloader version than this launcher has.",
        null => "The server sent an unexpected reply.",
        _ => $"The server replied with code {Result}.",
    };
}

/// <summary>
/// Speaks the same small protocol xiloader uses on the login server's auth port (54231): TLS, then one JSON request
/// and one JSON reply. This lets a friend create their account on a host's server straight from the launcher.
/// </summary>
public sealed class LoginClient(byte[] xiloaderVersion)
{
    public const int AuthPort = 54231;

    private const byte CommandNoop = 0x00;
    private const byte CommandCreate = 0x20;

    public static LoginClient ForXiloaderVersion(string version) =>
        new(version.Split('.').Select(byte.Parse).Concat(Enumerable.Repeat((byte)0, 3)).Take(3).ToArray());

    public Task<LoginReply> CreateAccountAsync(string host, string username, string password, CancellationToken cancellationToken = default) =>
        SendAsync(host, CommandCreate, username, password, expectReply: true, cancellationToken);

    /// <summary>Opens a TLS session to the auth port and sends a no-op; true when a LandSandBoat login server answered the handshake.</summary>
    public async Task<bool> PingAsync(string host, CancellationToken cancellationToken = default)
    {
        try
        {
            // Placeholder credentials: the server rejects empty ones (with a warning) before it even reads the command.
            await SendAsync(host, CommandNoop, "localxi", "ping", expectReply: false, cancellationToken);
            return true;
        }
        // BouncyCastle reports TLS failures as IOExceptions.
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<LoginReply> SendAsync(string host, byte command, string username, string password, bool expectReply, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        // The TLS library below works on blocking calls; these timeouts keep a silent server from hanging us.
        using var tcp = new TcpClient { ReceiveTimeout = 10_000, SendTimeout = 10_000 };
        await tcp.ConnectAsync(host, AuthPort, cts.Token);

        // LandSandBoat's login server only accepts TLS 1.3 (tlsv13_server in src/login/handler.h), and Windows 10's
        // built-in TLS can't make TLS 1.3 client connections. So, like xiloader bringing its own mbedTLS, use
        // BouncyCastle's managed TLS instead of SslStream.
        var protocol = new TlsClientProtocol(tcp.GetStream());
        try
        {
            await Task.Run(() => protocol.Connect(new LoginTlsClient()), cts.Token);
            var tls = protocol.Stream;

            var request = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
            {
                ["username"] = username,
                ["password"] = password,
                ["otp"] = "",
                ["new_password"] = "",
                ["version"] = xiloaderVersion.Select(b => (int)b).ToArray(),
                ["command"] = (int)command,
            });
            await Task.Run(() =>
            {
                tls.Write(request, 0, request.Length);
                tls.Flush();
            }, cts.Token);

            if (!expectReply)
            {
                return new LoginReply(null, null);
            }

            var buffer = new byte[4096];
            var read = await Task.Run(() => tls.Read(buffer, 0, buffer.Length), cts.Token);
            return ParseReply(buffer.AsSpan(0, read));
        }
        finally
        {
            try
            {
                protocol.Close();
            }
            catch (IOException)
            {
                // The server may already have closed its side.
            }
        }
    }

    /// <summary>TLS 1.3 (falling back to 1.2) with BouncyCastle's own crypto, independent of the Windows version.</summary>
    private sealed class LoginTlsClient() : DefaultTlsClient(new BcTlsCrypto(new SecureRandom()))
    {
        protected override ProtocolVersion[] GetSupportedVersions() => ProtocolVersion.TLSv13.DownTo(ProtocolVersion.TLSv12);

        public override TlsAuthentication GetAuthentication() => new SelfSignedServer();
    }

    /// <summary>
    /// The login server makes its own self-signed certificate on first run, so there is no authority to check it
    /// against (xiloader only warns too). The connection is still encrypted.
    /// </summary>
    private sealed class SelfSignedServer : TlsAuthentication
    {
        public void NotifyServerCertificate(TlsServerCertificate serverCertificate)
        {
        }

        public TlsCredentials GetClientCredentials(CertificateRequest certificateRequest) => null!;
    }

    internal static LoginReply ParseReply(ReadOnlySpan<byte> data)
    {
        var text = Encoding.UTF8.GetString(data).TrimEnd('\0');
        try
        {
            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;
            if (root.TryGetProperty("error_message", out var error) && error.GetString() is { Length: > 0 } message)
            {
                return new LoginReply(null, message);
            }

            return root.TryGetProperty("result", out var result) && result.TryGetByte(out var code)
                ? new LoginReply((LoginResult)code, null)
                : new LoginReply(null, null);
        }
        catch (JsonException)
        {
            return new LoginReply(null, null);
        }
    }
}
