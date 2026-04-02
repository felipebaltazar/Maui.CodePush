using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Maui.CodePush.Server.Data.Entities;

public class Patch
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    [BsonElement("appId")]
    [BsonRepresentation(BsonType.String)]
    public Guid AppId { get; set; }

    [BsonElement("releaseId")]
    [BsonRepresentation(BsonType.String)]
    public Guid ReleaseId { get; set; }

    [BsonElement("patchNumber")]
    public int PatchNumber { get; set; }

    [BsonElement("moduleName")]
    public string ModuleName { get; set; } = string.Empty;

    [BsonElement("version")]
    public string Version { get; set; } = string.Empty;

    [BsonElement("platform")]
    public string Platform { get; set; } = string.Empty;

    [BsonElement("channel")]
    public string Channel { get; set; } = "production";

    [BsonElement("dllHash")]
    public string DllHash { get; set; } = string.Empty;

    [BsonElement("dllSize")]
    public long DllSize { get; set; }

    [BsonElement("fileName")]
    public string FileName { get; set; } = string.Empty;

    [BsonElement("isMandatory")]
    public bool IsMandatory { get; set; }

    [BsonElement("rolloutPercentage")]
    public int RolloutPercentage { get; set; } = 100;

    [BsonElement("gitTag")]
    public string GitTag { get; set; } = string.Empty;

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }
}
