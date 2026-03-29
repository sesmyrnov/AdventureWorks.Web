using System.Text.Json.Serialization;

namespace AdventureWorks.Web.Models;

public class ProductModelDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("type")]
    public string Type => "productModel";

    [JsonPropertyName("productModelId")]
    public int ProductModelId { get; set; }

    [JsonPropertyName("productCategoryId")]
    public int ProductCategoryId { get; set; }              // Always 0 (synthetic)

    [JsonPropertyName("name")]
    public string Name { get; set; } = default!;

    [JsonPropertyName("catalogDescription")]
    public string? CatalogDescription { get; set; }

    [JsonPropertyName("descriptions")]
    public List<ProductDescriptionEntry> Descriptions { get; set; } = new();

    [JsonPropertyName("modifiedDate")]
    public DateTime ModifiedDate { get; set; }

    [JsonIgnore]
    public string? ETag { get; set; }

    public void AssignId() => Id = $"model-{ProductModelId}";
}
