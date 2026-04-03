using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Maui.CodePush.Server.Data.Entities;

public class Account
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    [BsonElement("email")]
    public string Email { get; set; } = string.Empty;

    [BsonElement("passwordHash")]
    public string PasswordHash { get; set; } = string.Empty;

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("apiKey")]
    public string ApiKey { get; set; } = string.Empty;

    [BsonElement("company")]
    public string? Company { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }
}
