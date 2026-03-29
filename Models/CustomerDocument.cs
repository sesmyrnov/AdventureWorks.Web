using System.Text.Json.Serialization;

namespace AdventureWorks.Web.Models;

public class CustomerDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("type")]
    public string Type => "customer";

    [JsonPropertyName("customerId")]
    public int CustomerId { get; set; }

    [JsonPropertyName("nameStyle")]
    public bool NameStyle { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("firstName")]
    public string FirstName { get; set; } = default!;

    [JsonPropertyName("middleName")]
    public string? MiddleName { get; set; }

    [JsonPropertyName("lastName")]
    public string LastName { get; set; } = default!;

    [JsonPropertyName("suffix")]
    public string? Suffix { get; set; }

    [JsonPropertyName("companyName")]
    public string? CompanyName { get; set; }

    [JsonPropertyName("salesPerson")]
    public string? SalesPerson { get; set; }

    [JsonPropertyName("emailAddress")]
    public string? EmailAddress { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("addresses")]
    public List<EmbeddedAddress> Addresses { get; set; } = new();

    [JsonPropertyName("modifiedDate")]
    public DateTime ModifiedDate { get; set; }

    [JsonIgnore]
    public string? ETag { get; set; }

    public void AssignId() => Id = $"customer-{CustomerId}";
}

public class EmbeddedAddress
{
    [JsonPropertyName("addressId")]
    public int AddressId { get; set; }

    [JsonPropertyName("addressType")]
    public string? AddressType { get; set; }

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
