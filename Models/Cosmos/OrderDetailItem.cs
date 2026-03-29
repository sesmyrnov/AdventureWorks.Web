using System.Text.Json.Serialization;

namespace AdventureWorks.Web.Models.Cosmos;

public class OrderDetailItem
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
}
