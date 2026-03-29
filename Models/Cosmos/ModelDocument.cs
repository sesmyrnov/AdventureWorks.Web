using System.Text.Json.Serialization;

namespace AdventureWorks.Web.Models.Cosmos;

public class ModelDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("partitionKey")]
    public string PartitionKey { get; set; } = default!;

    [JsonPropertyName("productModelId")]
    public int ProductModelId { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "model";

    [JsonPropertyName("name")]
    public string Name { get; set; } = default!;

    [JsonPropertyName("catalogDescription")]
    public object? CatalogDescription { get; set; }

    [JsonPropertyName("descriptions")]
    public List<ModelDescriptionItem> Descriptions { get; set; } = new();

    [JsonPropertyName("modifiedDate")]
    public string? ModifiedDate { get; set; }

    [JsonPropertyName("_schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}
