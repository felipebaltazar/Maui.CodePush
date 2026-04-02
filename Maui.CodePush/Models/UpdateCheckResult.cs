using System.Text.Json.Serialization;

namespace Maui.CodePush;

public class UpdateCheckResult
{
    [JsonPropertyName("updateAvailable")]
    public bool UpdateAvailable { get; set; }

    [JsonPropertyName("releaseVersion")]
    public string ReleaseVersion { get; set; } = string.Empty;

    [JsonPropertyName("patches")]
    public List<ModuleUpdateInfo> Patches { get; set; } = [];

    // Legacy compat
    [JsonPropertyName("modules")]
    public List<ModuleUpdateInfo> Modules { get; set; } = [];
}

public class ModuleUpdateInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("patchNumber")]
    public int PatchNumber { get; set; }

    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("isMandatory")]
    public bool IsMandatory { get; set; }
}
