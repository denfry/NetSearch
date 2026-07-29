using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetSearch.Core.Updates;

public sealed record UpdateInfo(
    Version Version,
    Uri ReleasePage,
    Uri InstallerUrl,
    Uri ChecksumUrl,
    string InstallerFileName);

public sealed class GitHubUpdateService
{
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/denfry/NetSearch/releases/latest";
    private const long MaxInstallerBytes = 512L * 1024 * 1024;
    private static readonly HashSet<string> TrustedDownloadHosts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "github.com",
            "objects.githubusercontent.com",
            "github-releases.githubusercontent.com",
        };

    private readonly HttpClient _http;

    public GitHubUpdateService(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10),
        };
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("NetSearch", "0.3"));
    }

    public async Task<UpdateInfo?> CheckAsync(Version currentVersion, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(body, cancellationToken: ct)
            ?? throw new InvalidDataException("GitHub returned an empty release.");

        if (!TryParseVersion(release.TagName, out var available) || available <= currentVersion)
            return null;

        var installerName = $"NetSearch-Setup-{available}.exe";
        var checksumName = installerName + ".sha256";
        var installer = release.Assets.SingleOrDefault(a =>
            string.Equals(a.Name, installerName, StringComparison.OrdinalIgnoreCase));
        var checksum = release.Assets.SingleOrDefault(a =>
            string.Equals(a.Name, checksumName, StringComparison.OrdinalIgnoreCase));
        if (installer is null || checksum is null)
            throw new InvalidDataException("The release does not contain a verified NetSearch installer.");

        var installerUrl = ParseTrustedDownloadUri(installer.DownloadUrl);
        var checksumUrl = ParseTrustedDownloadUri(checksum.DownloadUrl);
        var releasePage = ParseHttpsUri(release.HtmlUrl, "github.com");
        return new UpdateInfo(available, releasePage, installerUrl, checksumUrl, installerName);
    }

    public async Task<string> DownloadVerifiedAsync(
        UpdateInfo update,
        string destinationDirectory,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        ParseTrustedDownloadUri(update.InstallerUrl.ToString());
        ParseTrustedDownloadUri(update.ChecksumUrl.ToString());
        Directory.CreateDirectory(destinationDirectory);
        var destination = Path.GetFullPath(Path.Combine(destinationDirectory, update.InstallerFileName));
        var directory = Path.GetFullPath(destinationDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unsafe installer destination.");

        var expectedHash = await DownloadExpectedHash(update.ChecksumUrl, ct);
        var temporary = destination + ".download";
        try
        {
            using var response = await _http.GetAsync(
                update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var length = response.Content.Headers.ContentLength;
            if (length is > MaxInstallerBytes)
                throw new InvalidDataException("The installer is unexpectedly large.");

            string actualHash;
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var target = new FileStream(
                temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 128 * 1024, useAsync: true))
            using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[128 * 1024];
                long received = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    received += read;
                    if (received > MaxInstallerBytes)
                        throw new InvalidDataException("The installer is unexpectedly large.");
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    hasher.AppendData(buffer, 0, read);
                    if (length is > 0) progress?.Report((double)received / length.Value);
                }
                await target.FlushAsync(ct);
                actualHash = Convert.ToHexString(hasher.GetHashAndReset());
            }

            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actualHash), Convert.FromHexString(expectedHash)))
                throw new InvalidDataException("SHA-256 verification of the installer failed.");

            File.Move(temporary, destination, overwrite: true);
            progress?.Report(1);
            return destination;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { /* best effort */ }
        }
    }

    private async Task<string> DownloadExpectedHash(Uri checksumUrl, CancellationToken ct)
    {
        using var response = await _http.GetAsync(
            checksumUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 4096)
            throw new InvalidDataException("The release checksum file is unexpectedly large.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var memory = new MemoryStream(capacity: 256);
        var buffer = new byte[512];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (memory.Length + read > 4096)
                throw new InvalidDataException("The release checksum file is unexpectedly large.");
            memory.Write(buffer, 0, read);
        }
        var text = Encoding.ASCII.GetString(memory.GetBuffer(), 0, checked((int)memory.Length));
        var hash = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (hash is null || hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw new InvalidDataException("The release checksum is invalid.");
        return hash.ToUpperInvariant();
    }

    private static bool TryParseVersion(string tag, out Version version)
    {
        var normalized = tag.Trim().TrimStart('v', 'V').Split('-', 2)[0];
        return Version.TryParse(normalized, out version!);
    }

    private static Uri ParseTrustedDownloadUri(string raw)
    {
        var uri = ParseHttpsUri(raw);
        if (!TrustedDownloadHosts.Contains(uri.Host))
            throw new InvalidDataException("GitHub returned an untrusted download host.");
        return uri;
    }

    private static Uri ParseHttpsUri(string raw, string? requiredHost = null)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || (requiredHost is not null
                && !string.Equals(uri.Host, requiredHost, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("GitHub returned an invalid HTTPS URL.");
        return uri;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = "";

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; init; } = "";

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; init; } = new();
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        [JsonPropertyName("browser_download_url")]
        public string DownloadUrl { get; init; } = "";
    }
}
