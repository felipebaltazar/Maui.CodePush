using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Maui.CodePush.Server.Data.Entities;

public class AppRelease
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    [BsonElement("appId")]
    [BsonRepresentation(BsonType.String)]
    public Guid AppId { get; set; }

    [BsonElement("version")]
    public string Version { get; set; } = string.Empty;

    [BsonElement("platform")]
    public string Platform { get; set; } = string.Empty;

    [BsonElement("channel")]
    public string Channel { get; set; } = "production";

    [BsonElement("dependencySnapshot")]
    public List<ModuleDependencySnapshot> DependencySnapshot { get; set; } = [];

    [BsonElement("gitTag")]
    public string GitTag { get; set; } = string.Empty;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }
}

public class ModuleDependencySnapshot
{
    [BsonElement("moduleName")]
    public string ModuleName { get; set; } = string.Empty;

    [BsonElement("dllHash")]
    public string DllHash { get; set; } = string.Empty;

    [BsonElement("dllSize")]
    public long DllSize { get; set; }

    [BsonElement("assemblyReferences")]
    public List<AssemblyReferenceEntry> AssemblyReferences { get; set; } = [];
}

public class AssemblyReferenceEntry
{
    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("version")]
    public string Version { get; set; } = string.Empty;
}
