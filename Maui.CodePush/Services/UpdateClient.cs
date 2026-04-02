using System.Text.Json;

namespace Maui.CodePush;

public class UpdateClient
{
    private readonly HttpClient _httpClient;
    private readonly CodePushOptions _options;
    private readonly string _tempPath;

    internal UpdateClient(CodePushOptions options, string tempPath)
    {
        _options = options;
        _tempPath = tempPath;
        _httpClient = new HttpClient();

        if (!string.IsNullOrEmpty(options.ServerUrl))
            _httpClient.BaseAddress = new Uri(options.ServerUrl.TrimEnd('/') + "/");

        if (!string.IsNullOrEmpty(options.AppToken))
            _httpClient.DefaultRequestHeaders.Add("X-CodePush-Token", options.AppToken);
    }

    public async Task<UpdateCheckResult?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.ServerUrl) || string.IsNullOrEmpty(_options.AppId))
            return null;

        try
        {
            var platform = GetPlatform();
            var releaseVersion = _options.ReleaseVersion ?? "0.0.0";

            var url = $"api/updates/check?app={Uri.EscapeDataString(_options.AppId)}" +
                      $"&releaseVersion={Uri.EscapeDataString(releaseVersion)}" +
                      $"&platform={Uri.EscapeDataString(platform)}" +
                      $"&channel={Uri.EscapeDataString(_options.Channel)}";

            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<UpdateCheckResult>(json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CodePush] Update check failed: {ex.Message}");
            return null;
        }
    }

    public async Task<string?> DownloadModuleAsync(
        ModuleUpdateInfo updateInfo,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tempFilePath = Path.Combine(_tempPath, $"{updateInfo.Name}.dll");

            using var response = await _httpClient.GetAsync(updateInfo.DownloadUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = File.Create(tempFilePath);
            await contentStream.CopyToAsync(fileStream, cancellationToken);
            fileStream.Close();

            if (!string.IsNullOrEmpty(updateInfo.Hash))
            {
                if (!ModuleManager.VerifyHash(tempFilePath, updateInfo.Hash))
                {
                    System.Diagnostics.Debug.WriteLine($"[CodePush] Hash mismatch for {updateInfo.Name}. Rejecting update.");
                    File.Delete(tempFilePath);
                    return null;
                }
            }

            return tempFilePath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CodePush] Download failed for {updateInfo.Name}: {ex.Message}");
            return null;
        }
    }

    private static string GetPlatform()
    {
#if ANDROID
        return "android";
#elif IOS
        return "ios";
#else
        return "unknown";
#endif
    }
}
