using System.Text.Json.Serialization;

namespace Maui.CodePush;

public class ModuleInfo
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "0.0.0";

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [JsonPropertyName("downloadedAt")]
    public DateTime? DownloadedAt { get; set; }

    [JsonPropertyName("appliedAt")]
    public DateTime? AppliedAt { get; set; }

    [JsonPropertyName("previousHash")]
    public string? PreviousHash { get; set; }

    [JsonPropertyName("patchNumber")]
    public int PatchNumber { get; set; }

    [JsonPropertyName("releaseVersion")]
    public string ReleaseVersion { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public ModuleStatus Status { get; set; } = ModuleStatus.Embedded;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ModuleStatus
{
    Embedded,
    Active,
    Pending,
    RolledBack
}
