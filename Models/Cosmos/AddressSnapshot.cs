using System.Text.Json.Serialization;

namespace AdventureWorks.Web.Models.Cosmos;

public class AddressSnapshot
{
    [JsonPropertyName("addressLine1")]
    public string AddressLine1 { get; set; } = default!;

    [JsonPropertyName("addressLine2")]
    public string? AddressLine2 { get; set; }

    [JsonPropertyName("city")]
    public string City { get; set; } = default!;

    [JsonPropertyName("stateProvince")]
    public string StateProvince { get; set; } = default!;

    [JsonPropertyName("countryRegion")]
    public string CountryRegion { get; set; } = default!;

    [JsonPropertyName("postalCode")]
    public string PostalCode { get; set; } = default!;
}
