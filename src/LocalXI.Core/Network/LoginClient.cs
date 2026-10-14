using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

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
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or System.Security.Authentication.AuthenticationException)
        {
            return false;
        }
    }

    private async Task<LoginReply> SendAsync(string host, byte command, string username, string password, bool expectReply, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(host, AuthPort, cts.Token);

        // The login server generates a self-signed certificate on first run, so there is no CA to validate against
        // (xiloader itself only warns). The connection is still encrypted.
        await using var tls = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false, (_, _, _, _) => true);
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, cts.Token);

        var request = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["username"] = username,
            ["password"] = password,
            ["otp"] = "",
            ["new_password"] = "",
            ["version"] = xiloaderVersion.Select(b => (int)b).ToArray(),
            ["command"] = (int)command,
        });
        await tls.WriteAsync(request, cts.Token);
        await tls.FlushAsync(cts.Token);

        if (!expectReply)
        {
            return new LoginReply(null, null);
        }

        var buffer = new byte[4096];
        var read = await tls.ReadAsync(buffer, cts.Token);
        return ParseReply(buffer.AsSpan(0, read));
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
