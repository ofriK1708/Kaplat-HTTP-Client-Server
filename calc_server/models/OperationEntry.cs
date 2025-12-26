using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;

namespace calc_server.models;

[Table("operations")] // Table name in Postgres 
public class OperationEntry
{
    [Key] // Primary key for Postgres
    [BsonId] // Primary key for Mongo
    [BsonElement("rawid")] // Name in Mongo
    [JsonPropertyName("id")] // the display name in JSON
    public int rawid { get; set; }

    public string flavor { get; set; } = string.Empty; // STACK or INDEPENDENT 
    
    public string operation { get; set; } = string.Empty; // Operation name 
    
    public int result { get; set; } // Calculation result 
    
    public string arguments { get; set; } = string.Empty;

    public static OperationEntry FromHistoryEntry(HistoryEntry historyEntry)
    {
        OperationEntry entry = new OperationEntry();
        if (historyEntry.flavor == null || historyEntry.operation == null)
        {
            throw new ArgumentException("Invalid operation entry, flavor and operation must not be null");
        }
        entry.flavor = historyEntry.flavor;
        entry.operation = historyEntry.operation;
        entry.result = historyEntry.result;
        entry.arguments = JsonSerializer.Serialize(historyEntry.arguments);
        return entry;
    }
}