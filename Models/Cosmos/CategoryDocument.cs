using System.Text.Json.Serialization;

namespace AdventureWorks.Web.Models.Cosmos;

public class CategoryDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("partitionKey")]
    public string PartitionKey { get; set; } = default!;

    [JsonPropertyName("productCategoryId")]
    public int ProductCategoryId { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "category";

    [JsonPropertyName("name")]
    public string Name { get; set; } = default!;

    [JsonPropertyName("parentProductCategoryId")]
    public int? ParentProductCategoryId { get; set; }

    [JsonPropertyName("parentCategoryName")]
    public string? ParentCategoryName { get; set; }

    [JsonPropertyName("modifiedDate")]
    public string? ModifiedDate { get; set; }

    [JsonPropertyName("_schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}
