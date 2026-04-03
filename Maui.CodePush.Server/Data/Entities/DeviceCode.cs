using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Maui.CodePush.Server.Data.Entities;

public class DeviceCode
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    [BsonElement("deviceCode")]
    public string Code { get; set; } = string.Empty;

    [BsonElement("userCode")]
    public string UserCode { get; set; } = string.Empty;

    [BsonElement("accountId")]
    [BsonRepresentation(BsonType.String)]
    public Guid? AccountId { get; set; }

    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public DeviceCodeStatus Status { get; set; } = DeviceCodeStatus.Pending;

    [BsonElement("expiresAt")]
    public DateTime ExpiresAt { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }
}

public enum DeviceCodeStatus
{
    Pending,
    Authorized,
    Expired
}
