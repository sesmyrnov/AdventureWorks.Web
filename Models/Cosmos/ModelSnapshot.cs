using System.Text.Json.Serialization;

namespace AdventureWorks.Web.Models.Cosmos;

public class ModelSnapshot
{
    [JsonPropertyName("productModelId")]
    public int ProductModelId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = default!;
}
