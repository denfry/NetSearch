using System.Net;
using System.Security.Cryptography;
using System.Text;
using NetSearch.Core.Updates;
using Xunit;

namespace NetSearch.Core.Tests;

public sealed class GitHubUpdateServiceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"netsearch_update_{Guid.NewGuid():N}");

    [Fact]
    public async Task Check_and_download_accepts_only_the_hash_verified_installer()
    {
        var installerBytes = Encoding.UTF8.GetBytes("test installer bytes");
        var checksum = Convert.ToHexString(SHA256.HashData(installerBytes)).ToLowerInvariant();
        using var http = new HttpClient(new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/releases/latest"))
                return JsonResponse(ReleaseJson("9.9.9"));
            if (path.EndsWith(".sha256"))
                return TextResponse($"{checksum} *NetSearch-Setup-9.9.9.exe");
            return BytesResponse(installerBytes);
        }));
        var service = new GitHubUpdateService(http);

        var update = await service.CheckAsync(new Version(0, 3, 0), CancellationToken.None);
        Assert.NotNull(update);
        var path = await service.DownloadVerifiedAsync(
            update!, _directory, null, CancellationToken.None);

        Assert.Equal(installerBytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Download_rejects_a_hash_mismatch_and_leaves_no_executable()
    {
        var installerBytes = Encoding.UTF8.GetBytes("tampered");
        using var http = new HttpClient(new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/releases/latest"))
                return JsonResponse(ReleaseJson("9.9.9"));
            if (path.EndsWith(".sha256"))
                return TextResponse(new string('0', 64));
            return BytesResponse(installerBytes);
        }));
        var service = new GitHubUpdateService(http);
        var update = await service.CheckAsync(new Version(0, 3, 0), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.DownloadVerifiedAsync(update!, _directory, null, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(_directory, update!.InstallerFileName)));
        Assert.False(File.Exists(Path.Combine(_directory, update.InstallerFileName + ".download")));
    }

    [Fact]
    public async Task Check_returns_null_when_current_version_is_latest()
    {
        using var http = new HttpClient(new StubHandler(_ => JsonResponse(ReleaseJson("0.3.0"))));
        var service = new GitHubUpdateService(http);

        var update = await service.CheckAsync(new Version(0, 3, 0), CancellationToken.None);

        Assert.Null(update);
    }

    private static string ReleaseJson(string version) => $$"""
        {
          "tag_name": "v{{version}}",
          "html_url": "https://github.com/denfry/NetSearch/releases/tag/v{{version}}",
          "assets": [
            {
              "name": "NetSearch-Setup-{{version}}.exe",
              "browser_download_url": "https://github.com/denfry/NetSearch/releases/download/v{{version}}/NetSearch-Setup-{{version}}.exe"
            },
            {
              "name": "NetSearch-Setup-{{version}}.exe.sha256",
              "browser_download_url": "https://github.com/denfry/NetSearch/releases/download/v{{version}}/NetSearch-Setup-{{version}}.exe.sha256"
            }
          ]
        }
        """;

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage TextResponse(string text) =>
        new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.ASCII, "text/plain") };

    private static HttpResponseMessage BytesResponse(byte[] bytes) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
