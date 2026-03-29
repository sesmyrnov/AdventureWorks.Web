using System.Text.Json.Serialization;

namespace AdventureWorks.Web.Models.Cosmos;

public class CustomerDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("customerId")]
    public int CustomerId { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "customer";

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

    [JsonPropertyName("passwordHash")]
    public string PasswordHash { get; set; } = default!;

    [JsonPropertyName("passwordSalt")]
    public string PasswordSalt { get; set; } = default!;

    [JsonPropertyName("addresses")]
    public List<EmbeddedAddress> Addresses { get; set; } = new();

    [JsonPropertyName("modifiedDate")]
    public string? ModifiedDate { get; set; }

    [JsonPropertyName("ttl")]
    public int Ttl { get; set; } = -1;

    [JsonPropertyName("_schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}
