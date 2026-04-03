using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Maui.CodePush.Cli.Services;

public class ServerClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public ServerClient(string serverUrl, string? token = null, string? apiKey = null)
    {
        _baseUrl = serverUrl.TrimEnd('/');
        _http = new HttpClient { BaseAddress = new Uri(_baseUrl + "/") };

        if (!string.IsNullOrEmpty(apiKey))
            _http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        else if (!string.IsNullOrEmpty(token))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<JsonElement> LoginAsync(string email, string password)
    {
        var response = await _http.PostAsJsonAsync("api/auth/login", new { email, password });

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Login failed ({response.StatusCode}): {err}");
        }

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<JsonElement> CreateDeviceCodeAsync()
    {
        var response = await _http.PostAsync("api/auth/device", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<JsonElement> PollDeviceTokenAsync(string deviceCode)
    {
        var response = await _http.PostAsJsonAsync("api/auth/device/token", new { deviceCode });
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<JsonElement> GetMeAsync()
    {
        var response = await _http.GetAsync("api/auth/me");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<JsonElement> ListAppsAsync()
    {
        var response = await _http.GetAsync("api/apps");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<JsonElement> CreateAppAsync(string packageName, string displayName)
    {
        var response = await _http.PostAsJsonAsync("api/apps", new { packageName, displayName });

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Create app failed ({response.StatusCode}): {err}");
        }

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<JsonElement> UploadReleaseAsync(
        string appId, string dllPath, string moduleName, string version, string platform, string channel = "production")
    {
        using var form = new MultipartFormDataContent();

        var fileBytes = await File.ReadAllBytesAsync(dllPath);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "file", Path.GetFileName(dllPath));

        form.Add(new StringContent(moduleName), "moduleName");
        form.Add(new StringContent(version), "version");
        form.Add(new StringContent(platform), "platform");
        form.Add(new StringContent(channel), "channel");

        var response = await _http.PostAsync($"api/apps/{appId}/releases", form);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Upload failed ({response.StatusCode}): {err}");
        }

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<JsonElement> ListReleasesAsync(string appId)
    {
        var response = await _http.GetAsync($"api/apps/{appId}/releases");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // ── New Release/Patch API (v2) ──────────────────────────────

    public async Task<JsonElement> CreateAppReleaseAsync(
        string appId, string version, string platform, string channel,
        List<(string moduleName, string dllPath)> modules,
        string dependencySnapshotJson)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(version), "version");
        form.Add(new StringContent(platform), "platform");
        form.Add(new StringContent(channel), "channel");
        form.Add(new StringContent(dependencySnapshotJson), "dependencySnapshot");
        form.Add(new StringContent(string.Join(",", modules.Select(m => m.moduleName))), "moduleNames");

        foreach (var (moduleName, dllPath) in modules)
        {
            var fileBytes = await File.ReadAllBytesAsync(dllPath);
            var content = new ByteArrayContent(fileBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(content, "files", $"{moduleName}.dll");
        }

        var response = await _http.PostAsync($"api/apps/{appId}/releases/v2", form);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Create release failed ({response.StatusCode}): {err}");
        }
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<JsonElement> GetAppReleaseAsync(string appId, string version, string platform, string channel = "production")
    {
        var response = await _http.GetAsync(
            $"api/apps/{appId}/releases/v2?version={Uri.EscapeDataString(version)}&platform={Uri.EscapeDataString(platform)}&channel={Uri.EscapeDataString(channel)}");

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Get release failed ({response.StatusCode}): {err}");
        }
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<JsonElement> ListAppReleasesAsync(string appId)
    {
        var response = await _http.GetAsync($"api/apps/{appId}/releases/v2");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<JsonElement> CreatePatchAsync(
        string appId, string releaseId, string dllPath, string moduleName,
        string channel = "production", bool isMandatory = false, int rolloutPercentage = 100)
    {
        using var form = new MultipartFormDataContent();

        var fileBytes = await File.ReadAllBytesAsync(dllPath);
        var content = new ByteArrayContent(fileBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(content, "file", Path.GetFileName(dllPath));

        form.Add(new StringContent(moduleName), "moduleName");
        form.Add(new StringContent(channel), "channel");
        form.Add(new StringContent(isMandatory.ToString()), "isMandatory");
        form.Add(new StringContent(rolloutPercentage.ToString()), "rolloutPercentage");

        var response = await _http.PostAsync($"api/apps/{appId}/releases/{releaseId}/patches", form);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Create patch failed ({response.StatusCode}): {err}");
        }
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<JsonElement> ListPatchesAsync(string appId, string releaseId)
    {
        var response = await _http.GetAsync($"api/apps/{appId}/releases/{releaseId}/patches");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
