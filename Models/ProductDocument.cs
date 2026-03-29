using System.Text.Json.Serialization;

namespace AdventureWorks.Web.Models;

public class ProductDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("type")]
    public string Type => "product";

    [JsonPropertyName("productId")]
    public int ProductId { get; set; }

    [JsonPropertyName("productCategoryId")]
    public int ProductCategoryId { get; set; }

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

    [JsonPropertyName("categoryName")]
    public string? CategoryName { get; set; }

    [JsonPropertyName("parentCategoryName")]
    public string? ParentCategoryName { get; set; }

    [JsonPropertyName("productModelId")]
    public int? ProductModelId { get; set; }

    [JsonPropertyName("productModelName")]
    public string? ProductModelName { get; set; }

    [JsonPropertyName("descriptions")]
    public List<ProductDescriptionEntry> Descriptions { get; set; } = new();

    [JsonPropertyName("sellStartDate")]
    public DateTime SellStartDate { get; set; }

    [JsonPropertyName("sellEndDate")]
    public DateTime? SellEndDate { get; set; }

    [JsonPropertyName("discontinuedDate")]
    public DateTime? DiscontinuedDate { get; set; }

    [JsonPropertyName("thumbnailPhotoUrl")]
    public string? ThumbnailPhotoUrl { get; set; }

    [JsonPropertyName("thumbnailPhotoFileName")]
    public string? ThumbnailPhotoFileName { get; set; }

    [JsonPropertyName("modifiedDate")]
    public DateTime ModifiedDate { get; set; }

    [JsonIgnore]
    public string? ETag { get; set; }

    public void AssignId() => Id = $"product-{ProductId}";
}

public class ProductDescriptionEntry
{
    [JsonPropertyName("culture")]
    public string Culture { get; set; } = default!;

    [JsonPropertyName("description")]
    public string Description { get; set; } = default!;
}
