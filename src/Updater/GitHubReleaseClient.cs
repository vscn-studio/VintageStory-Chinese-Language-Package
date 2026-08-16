using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace VscnLanguagePackUpdater;

internal sealed class GitHubReleaseClient : IDisposable
{
    private const int MaxMetadataBytes = 4 * 1024 * 1024;
    private const long MaxDownloadBytes = 128L * 1024 * 1024;
    private const int MaxRedirects = 5;
    private readonly HttpClient httpClient;

    public GitHubReleaseClient(TimeSpan timeout)
    {
        httpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = System.Net.DecompressionMethods.All
        })
        {
            Timeout = timeout
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UpdaterConstants.UserAgent);
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<GitHubRelease> GetLatestReleaseAsync(
        IReadOnlyList<string> apiUrls,
        IReadOnlyList<string> latestReleaseUrls,
        Func<string, IReadOnlyList<GitHubReleaseAsset>> createFallbackAssets,
        Action<string, string>? onAttemptFailed,
        CancellationToken cancellationToken)
    {
        var failureCount = 0;
        var apiCandidates = NormalizeCandidateUrls(apiUrls).ToArray();
        // Keep both official latest pages as final fallbacks even if a user configures
        // an invalid or incomplete releasesLatestUrl value.
        var latestReleaseCandidates = NormalizeCandidateUrls(
                latestReleaseUrls
                    .Append(UpdaterConstants.ReleasesLatestUrl)
                    .Append(UpdaterConstants.VscnStudioLatestUrl))
            .ToArray();
        var candidateCount = Math.Max(apiCandidates.Length, latestReleaseCandidates.Length);

        for (var index = 0; index < candidateCount; index++)
        {
            if (index < apiCandidates.Length)
            {
                var apiUrl = apiCandidates[index];
                try
                {
                    return await GetLatestReleaseFromUrlAsync(apiUrl, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failureCount++;
                    onAttemptFailed?.Invoke(apiUrl, ex.Message);
                }
            }

            if (index < latestReleaseCandidates.Length)
            {
                var latestReleaseUrl = latestReleaseCandidates[index];
                try
                {
                    return await GetLatestReleaseFromRedirectAsync(
                        latestReleaseUrl,
                        createFallbackAssets,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failureCount++;
                    onAttemptFailed?.Invoke(latestReleaseUrl, ex.Message);
                }
            }
        }

        throw CreateAllChannelsFailedException("获取发布信息", failureCount);
    }

    private async Task<GitHubRelease> GetLatestReleaseFromUrlAsync(string apiUrl, CancellationToken cancellationToken)
    {
        var safeResponse = await SendSafeGetAsync(apiUrl, cancellationToken);
        using var response = safeResponse.Response;
        response.EnsureSuccessStatusCode();

        var payload = await ReadContentBytesAsync(response, MaxMetadataBytes, cancellationToken);
        return ParseReleaseJson(payload, safeResponse.FinalUri.AbsoluteUri);
    }

    private async Task<GitHubRelease> GetLatestReleaseFromRedirectAsync(
        string latestReleaseUrl,
        Func<string, IReadOnlyList<GitHubReleaseAsset>> createFallbackAssets,
        CancellationToken cancellationToken)
    {
        var safeResponse = await SendSafeGetAsync(latestReleaseUrl, cancellationToken);
        using var response = safeResponse.Response;
        response.EnsureSuccessStatusCode();

        var payload = await ReadContentBytesAsync(response, MaxMetadataBytes, cancellationToken);
        var firstContentByte = payload.FirstOrDefault(value => !char.IsWhiteSpace((char)value));
        if (firstContentByte == (byte)'{')
        {
            return ParseReleaseJson(payload, safeResponse.FinalUri.AbsoluteUri);
        }

        var finalUrl = safeResponse.FinalUri.AbsoluteUri;
        var tagName = ExtractTagNameFromReleaseUrl(finalUrl)
            ?? throw new InvalidOperationException("Latest release redirect did not contain a release tag.");

        var assets = createFallbackAssets(tagName);
        if (assets.Count == 0)
        {
            throw new InvalidOperationException($"Could not infer release assets from tag '{tagName}'.");
        }

        return new GitHubRelease(tagName, assets, finalUrl);
    }

    private static GitHubRelease ParseReleaseJson(byte[] payload, string sourceUrl)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var tagName = GetRequiredString(root, "tag_name");
        var assets = new List<GitHubReleaseAsset>();
        if (root.TryGetProperty("assets", out var assetsElement) && assetsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsElement.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object ||
                    !asset.TryGetProperty("name", out var nameElement) ||
                    !asset.TryGetProperty("browser_download_url", out var urlElement))
                {
                    continue;
                }

                var name = nameElement.GetString();
                var url = urlElement.GetString();
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                assets.Add(new GitHubReleaseAsset(name.Trim(), url.Trim(), TryGetSha256Digest(asset)));
            }
        }

        return new GitHubRelease(tagName, assets, sourceUrl);
    }

    public async Task<string> DownloadFileAsync(
        IReadOnlyList<string> urls,
        string destinationPath,
        string? expectedSha256,
        Action<string, string>? onAttemptFailed,
        CancellationToken cancellationToken)
    {
        var failureCount = 0;
        foreach (var url in NormalizeCandidateUrls(urls))
        {
            try
            {
                await DownloadFileFromUrlAsync(url, destinationPath, expectedSha256, cancellationToken);
                return url;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failureCount++;
                TryDeleteFile(destinationPath);
                onAttemptFailed?.Invoke(url, ex.Message);
            }
        }

        throw CreateAllChannelsFailedException("下载汉化包", failureCount);
    }

    public Task<string> DownloadFileAsync(
        IReadOnlyList<string> urls,
        string destinationPath,
        Action<string, string>? onAttemptFailed,
        CancellationToken cancellationToken)
    {
        return DownloadFileAsync(urls, destinationPath, null, onAttemptFailed, cancellationToken);
    }

    private async Task DownloadFileFromUrlAsync(
        string url,
        string destinationPath,
        string? expectedSha256,
        CancellationToken cancellationToken)
    {
        var safeResponse = await SendSafeGetAsync(url, cancellationToken);
        using var response = safeResponse.Response;
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > MaxDownloadBytes)
        {
            throw new InvalidOperationException("下载响应超过允许的文件大小限制。");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long totalBytes = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            totalBytes += read;
            if (totalBytes > MaxDownloadBytes)
            {
                throw new InvalidOperationException("下载响应超过允许的文件大小限制。");
            }

            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        await destination.FlushAsync(cancellationToken);
        destination.Flush(flushToDisk: true);

        var normalizedExpectedHash = NormalizeSha256Digest(expectedSha256);
        if (normalizedExpectedHash is not null)
        {
            var actualHash = Convert.ToHexString(hash.GetHashAndReset());
            if (!string.Equals(actualHash, normalizedExpectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("下载文件 SHA-256 校验失败。");
            }
        }
    }

    private async Task<SafeResponse> SendSafeGetAsync(string url, CancellationToken cancellationToken)
    {
        var currentUri = ValidateRequestUri(url);
        for (var redirect = 0; ; redirect++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)response.StatusCode is >= 300 and <= 399)
            {
                if (redirect >= MaxRedirects || response.Headers.Location is null)
                {
                    response.Dispose();
                    throw new InvalidOperationException("下载地址重定向次数过多或缺少目标地址。");
                }

                var nextUri = new Uri(currentUri, response.Headers.Location);
                response.Dispose();
                currentUri = ValidateRequestUri(nextUri.ToString());
                continue;
            }

            return new SafeResponse(response, currentUri);
        }
    }

    private static Uri ValidateRequestUri(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("下载地址不是有效的绝对 URL。");
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !uri.IsDefaultPort ||
            !UpdaterConstants.TrustedDownloadHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"下载地址主机不在受信任列表中：{uri.Host}。");
        }

        return uri;
    }

    private static async Task<byte[]> ReadContentBytesAsync(
        HttpResponseMessage response,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is long contentLength && contentLength > maxBytes)
        {
            throw new InvalidOperationException("响应内容超过允许的大小限制。");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > maxBytes)
            {
                throw new InvalidOperationException("响应内容超过允许的大小限制。");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static string? TryGetSha256Digest(JsonElement asset)
    {
        if (!asset.TryGetProperty("digest", out var digestElement) ||
            digestElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return NormalizeSha256Digest(digestElement.GetString());
    }

    private static string? NormalizeSha256Digest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            return null;
        }

        var normalized = digest.Trim();
        if (normalized.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["sha256:".Length..];
        }

        return normalized.Length == 64 && normalized.All(Uri.IsHexDigit)
            ? normalized.ToUpperInvariant()
            : throw new InvalidOperationException("Release 提供了无效的 SHA-256 摘要。");
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }

    private static IEnumerable<string> NormalizeCandidateUrls(IEnumerable<string> urls)
    {
        return urls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static InvalidOperationException CreateAllChannelsFailedException(string operation, int failureCount)
    {
        return new InvalidOperationException(
            $"{operation}所有 {failureCount} 个通道均失败，请检查网络或更换加速通道。");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static string? ExtractTagNameFromReleaseUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var decoded = Uri.UnescapeDataString(url);
        const string marker = "/releases/tag/";
        var markerIndex = decoded.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var start = markerIndex + marker.Length;
        var end = decoded.IndexOfAny(['?', '#', '/'], start);
        var tagName = end < 0 ? decoded[start..] : decoded[start..end];
        return string.IsNullOrWhiteSpace(tagName) ? null : tagName.Trim();
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(property.GetString()))
        {
            return property.GetString()!.Trim();
        }

        throw new InvalidOperationException($"GitHub release response did not contain '{propertyName}'.");
    }
}

internal sealed record GitHubRelease(
    string TagName,
    IReadOnlyList<GitHubReleaseAsset> Assets,
    string SourceUrl = "");

internal sealed record GitHubReleaseAsset(string Name, string DownloadUrl, string? Sha256 = null);

internal sealed record SafeResponse(HttpResponseMessage Response, Uri FinalUri);
