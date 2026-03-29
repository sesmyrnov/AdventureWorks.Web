using System.Text.Json.Serialization;

namespace AdventureWorks.Web.Models.Cosmos;

public class SalesOrderDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("salesOrderId")]
    public int SalesOrderId { get; set; }

    [JsonPropertyName("customerId")]
    public int CustomerId { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "salesOrder";

    [JsonPropertyName("revisionNumber")]
    public int RevisionNumber { get; set; }

    [JsonPropertyName("orderDate")]
    public string? OrderDate { get; set; }

    [JsonPropertyName("dueDate")]
    public string? DueDate { get; set; }

    [JsonPropertyName("shipDate")]
    public string? ShipDate { get; set; }

    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("onlineOrderFlag")]
    public bool OnlineOrderFlag { get; set; }

    [JsonPropertyName("salesOrderNumber")]
    public string SalesOrderNumber { get; set; } = default!;

    [JsonPropertyName("purchaseOrderNumber")]
    public string? PurchaseOrderNumber { get; set; }

    [JsonPropertyName("accountNumber")]
    public string? AccountNumber { get; set; }

    [JsonPropertyName("shipMethod")]
    public string? ShipMethod { get; set; }

    [JsonPropertyName("creditCardApprovalCode")]
    public string? CreditCardApprovalCode { get; set; }

    [JsonPropertyName("subTotal")]
    public decimal SubTotal { get; set; }

    [JsonPropertyName("taxAmt")]
    public decimal TaxAmt { get; set; }

    [JsonPropertyName("freight")]
    public decimal Freight { get; set; }

    [JsonPropertyName("totalDue")]
    public decimal TotalDue { get; set; }

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    [JsonPropertyName("shipToAddress")]
    public AddressSnapshot? ShipToAddress { get; set; }

    [JsonPropertyName("billToAddress")]
    public AddressSnapshot? BillToAddress { get; set; }

    [JsonPropertyName("details")]
    public List<OrderDetailItem> Details { get; set; } = new();

    [JsonPropertyName("modifiedDate")]
    public string? ModifiedDate { get; set; }

    [JsonPropertyName("ttl")]
    public int Ttl { get; set; } = 63072000;

    [JsonPropertyName("_schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}
