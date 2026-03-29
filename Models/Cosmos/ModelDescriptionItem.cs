using System.Text.Json.Serialization;

namespace AdventureWorks.Web.Models.Cosmos;

public class ModelDescriptionItem
{
    [JsonPropertyName("culture")]
    public string Culture { get; set; } = default!;

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
