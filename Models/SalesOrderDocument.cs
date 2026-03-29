using System.Text.Json.Serialization;

namespace AdventureWorks.Web.Models;

public class SalesOrderDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("type")]
    public string Type => "salesOrder";

    [JsonPropertyName("salesOrderId")]
    public int SalesOrderId { get; set; }

    [JsonPropertyName("customerId")]
    public int CustomerId { get; set; }

    [JsonPropertyName("revisionNumber")]
    public int RevisionNumber { get; set; }

    [JsonPropertyName("orderDate")]
    public DateTime OrderDate { get; set; }

    [JsonPropertyName("dueDate")]
    public DateTime DueDate { get; set; }

    [JsonPropertyName("shipDate")]
    public DateTime? ShipDate { get; set; }

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

    [JsonPropertyName("orderDetails")]
    public List<OrderDetailLine> OrderDetails { get; set; } = new();

    [JsonPropertyName("modifiedDate")]
    public DateTime ModifiedDate { get; set; }

    [JsonIgnore]
    public string? ETag { get; set; }

    public void AssignId() => Id = $"order-{SalesOrderId}";

    public void ComputeDerivedFields()
    {
        SalesOrderNumber = $"SO{SalesOrderId}";
        TotalDue = SubTotal + TaxAmt + Freight;
        foreach (var d in OrderDetails)
            d.ComputeLineTotal();
    }
}

public class AddressSnapshot
{
    [JsonPropertyName("addressLine1")]
    public string? AddressLine1 { get; set; }

    [JsonPropertyName("addressLine2")]
    public string? AddressLine2 { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("stateProvince")]
    public string? StateProvince { get; set; }

    [JsonPropertyName("countryRegion")]
    public string? CountryRegion { get; set; }

    [JsonPropertyName("postalCode")]
    public string? PostalCode { get; set; }
}

public class OrderDetailLine
{
    [JsonPropertyName("salesOrderDetailId")]
    public int SalesOrderDetailId { get; set; }

    [JsonPropertyName("productId")]
    public int ProductId { get; set; }

    [JsonPropertyName("productName")]
    public string? ProductName { get; set; }

    [JsonPropertyName("productNumber")]
    public string? ProductNumber { get; set; }

    [JsonPropertyName("orderQty")]
    public int OrderQty { get; set; }

    [JsonPropertyName("unitPrice")]
    public decimal UnitPrice { get; set; }

    [JsonPropertyName("unitPriceDiscount")]
    public decimal UnitPriceDiscount { get; set; }

    [JsonPropertyName("lineTotal")]
    public decimal LineTotal { get; set; }

    public void ComputeLineTotal()
    {
        LineTotal = UnitPrice * (1.0m - UnitPriceDiscount) * OrderQty;
    }
}
