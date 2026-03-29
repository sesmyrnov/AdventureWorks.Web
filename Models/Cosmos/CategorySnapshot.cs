using System.Text.Json.Serialization;

namespace AdventureWorks.Web.Models.Cosmos;

public class CategorySnapshot
{
    [JsonPropertyName("productCategoryId")]
    public int ProductCategoryId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = default!;

    [JsonPropertyName("parentCategoryName")]
    public string? ParentCategoryName { get; set; }
}
