using System.Text.Json.Serialization;

namespace AdventureWorks.Web.Models.Cosmos;

public class ProductDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("partitionKey")]
    public string PartitionKey { get; set; } = default!;

    [JsonPropertyName("productId")]
    public int ProductId { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "product";

    [JsonPropertyName("name")]
    public string Name { get; set; } = default!;

    [JsonPropertyName("productNumber")]
    public string ProductNumber { get; set; } = default!;

    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("standardCost")]
    public decimal StandardCost { get; set; }

    [JsonPropertyName("listPrice")]
    public decimal ListPrice { get; set; }

    [JsonPropertyName("size")]
    public string? Size { get; set; }

    [JsonPropertyName("weight")]
    public decimal? Weight { get; set; }

    [JsonPropertyName("productCategoryId")]
    public int? ProductCategoryId { get; set; }

    [JsonPropertyName("category")]
    public CategorySnapshot? Category { get; set; }

    [JsonPropertyName("productModelId")]
    public int? ProductModelId { get; set; }

    [JsonPropertyName("model")]
    public ModelSnapshot? Model { get; set; }

    [JsonPropertyName("sellStartDate")]
    public string? SellStartDate { get; set; }

    [JsonPropertyName("sellEndDate")]
    public string? SellEndDate { get; set; }

    [JsonPropertyName("discontinuedDate")]
    public string? DiscontinuedDate { get; set; }

    [JsonPropertyName("thumbnailPhotoUrl")]
    public string? ThumbnailPhotoUrl { get; set; }

    [JsonPropertyName("modifiedDate")]
    public string? ModifiedDate { get; set; }

    [JsonPropertyName("_schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}
