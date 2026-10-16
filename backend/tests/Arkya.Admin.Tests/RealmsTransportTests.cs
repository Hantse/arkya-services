using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Arkya.Admin.Realms;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Arkya.Admin.Tests;

public sealed class RealmsTransportTests
{
    [Theory]
    [InlineData("localhost", false, true, true)]
    [InlineData("wrong-host.example", false, true, false)]
    [InlineData("localhost", true, true, false)]
    [InlineData("localhost", false, false, false)]
    public async Task PrivateCaTrustRetainsHostnameExpiryAndChainValidation(string host, bool expired, bool trustCa, bool accepted)
    {
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest("CN=Arkya Admin Test CA", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddYears(-1), DateTimeOffset.UtcNow.AddYears(1));
        using var leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest("CN=" + host, leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(host);
        leafRequest.CertificateExtensions.Add(names.Build());
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        leafRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        using var signed = leafRequest.Create(root, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddHours(expired ? -1 : 1), RandomNumberGenerator.GetBytes(16));
        using var generated = signed.CopyWithPrivateKey(leafKey);
        using var leaf = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null,
            X509KeyStorageFlags.Exportable);
        var caFile = Path.Combine(Path.GetTempPath(), "arkya-admin-ca-" + Guid.NewGuid().ToString("N") + ".pem");
        await File.WriteAllTextAsync(caFile, root.ExportCertificatePem());
        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(leaf)));
            await using var app = builder.Build();
            app.MapGet("/", () => Results.Text("trusted realm service"));
            await app.StartAsync();
            using var handler = RealmsHttpTransport.Create(trustCa ? caFile : "");
            Assert.Null(handler.SslOptions.RemoteCertificateValidationCallback);
            Assert.False(handler.AllowAutoRedirect);
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            var url = app.Urls.Single().Replace("127.0.0.1", "localhost");
            if (accepted) Assert.Equal("trusted realm service", await client.GetStringAsync(url));
            else await Assert.ThrowsAsync<HttpRequestException>(() => client.GetStringAsync(url));
        }
        finally { File.Delete(caFile); }
    }
}
