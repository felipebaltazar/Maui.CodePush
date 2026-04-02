using System.Text.Json.Serialization;

namespace Maui.CodePush.Cli.Models;

public class CodePushConfig
{
    [JsonPropertyName("packageName")]
    public string PackageName { get; set; } = string.Empty;

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = "android";

    [JsonPropertyName("modules")]
    public List<ModuleConfig> Modules { get; set; } = [];

    [JsonPropertyName("outputDir")]
    public string? OutputDir { get; set; }

    [JsonPropertyName("adbPath")]
    public string? AdbPath { get; set; }

    // Server config (set by `codepush login`)
    [JsonPropertyName("serverUrl")]
    public string? ServerUrl { get; set; }

    [JsonPropertyName("appId")]
    public string? AppId { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }
}

public class ModuleConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("projectPath")]
    public string? ProjectPath { get; set; }
}
