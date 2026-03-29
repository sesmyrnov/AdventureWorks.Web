using System.Text.Json.Serialization;

namespace AdventureWorks.Web.Models;

public class ProductCategoryDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("type")]
    public string Type => "productCategory";

    [JsonPropertyName("productCategoryId")]
    public int ProductCategoryId { get; set; }

    [JsonPropertyName("parentProductCategoryId")]
    public int? ParentProductCategoryId { get; set; }

    [JsonPropertyName("parentCategoryName")]
    public string? ParentCategoryName { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = default!;

    [JsonPropertyName("modifiedDate")]
    public DateTime ModifiedDate { get; set; }

    [JsonIgnore]
    public string? ETag { get; set; }

    public void AssignId() => Id = $"category-{ProductCategoryId}";
}
