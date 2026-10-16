using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Arkya.Realms.Api.Infrastructure;

namespace Arkya.Realms.Tests;

public sealed class KubernetesTlsTests
{
    [Theory]
    [InlineData("localhost", false, true)]
    [InlineData("127.0.0.1", false, false)]
    [InlineData("localhost", true, false)]
    public async Task HttpsRequiresBothConfiguredAuthorityAndMatchingHostname(string hostname, bool foreignAuthority, bool succeeds)
    {
        using var authorityKey = RSA.Create(2048);
        using var authority = CreateAuthority(authorityKey, "Test Realm CA");
        using var foreignKey = RSA.Create(2048);
        using var foreign = CreateAuthority(foreignKey, "Unrelated CA");
        using var serverKey = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", serverKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        using var signed = request.Create(authority, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1), RandomNumberGenerator.GetBytes(16));
        using var certificateWithKey = signed.CopyWithPrivateKey(serverKey);
        // Schannel requires a user key container; disposal removes this temporary test key.
        using var serverCertificate = X509CertificateLoader.LoadPkcs12(certificateWithKey.Export(X509ContentType.Pkcs12), null,
            X509KeyStorageFlags.UserKeySet);
        var caFile = Path.GetTempFileName();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = ServeOnceAsync(listener, serverCertificate, succeeds, timeout.Token);
        try
        {
            await File.WriteAllTextAsync(caFile, (foreignAuthority ? foreign : authority).ExportCertificatePem(), timeout.Token);
            var options = Fixtures.Options();
            options.CertificateAuthorityFile = caFile;
            using var handler = KubernetesTransport.CreateHandler(options);
            handler.UseProxy = false;
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            var endpoint = $"https://{hostname}:{((IPEndPoint)listener.LocalEndpoint).Port}/";
            if (succeeds)
                Assert.Equal("ok", await client.GetStringAsync(endpoint, timeout.Token));
            else
                await Assert.ThrowsAsync<HttpRequestException>(() => client.GetStringAsync(endpoint, timeout.Token));
        }
        finally
        {
            await timeout.CancelAsync();
            listener.Stop();
            await server;
            File.Delete(caFile);
        }
    }

    private static X509Certificate2 CreateAuthority(RSA key, string name)
    {
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static async Task ServeOnceAsync(TcpListener listener, X509Certificate2 certificate, bool expectSuccess, CancellationToken cancellationToken)
    {
        try
        {
            using var socket = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var ssl = new SslStream(socket.GetStream());
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate }, cancellationToken);
            using var reader = new StreamReader(ssl, leaveOpen: true);
            while (await reader.ReadLineAsync(cancellationToken) is { Length: > 0 }) { }
            await ssl.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"), cancellationToken);
        }
        catch (Exception exception) when (!expectSuccess && exception is AuthenticationException or IOException or OperationCanceledException or SocketException)
        {
            // Certificate rejection deliberately aborts the server handshake in the negative cases.
        }
    }
}
