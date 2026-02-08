using System.ComponentModel.DataAnnotations;

namespace AdventureWorks.Web.Models;

/// <summary>
/// SalesOrder document — stored in the "customers" container with partition key /customerId.
/// Co-located with its parent Customer for efficient single-partition queries.
/// Embeds line items and address snapshots; denormalizes customerName and productName.
/// </summary>
public class SalesOrder
{
    public string Id { get; set; }

    /// <summary>Partition key — references the owning customer.</summary>
    public string CustomerId { get; set; }

    public string DocType { get; set; } = "salesOrder";

    public int SalesOrderId { get; set; }

    public int RevisionNumber { get; set; }

    [Display(Name = "Order Date")]
    public DateTime OrderDate { get; set; }

    [Display(Name = "Due Date")]
    public DateTime DueDate { get; set; }

    [Display(Name = "Ship Date")]
    public DateTime? ShipDate { get; set; }

    public int Status { get; set; }

    [Display(Name = "Online Order")]
    public bool OnlineOrderFlag { get; set; }

    [Display(Name = "Sales Order Number")]
    public string SalesOrderNumber { get; set; }

    [Display(Name = "PO Number")]
    public string PurchaseOrderNumber { get; set; }

    [Display(Name = "Account Number")]
    public string AccountNumber { get; set; }

    [Display(Name = "Ship Method")]
    public string ShipMethod { get; set; }

    [Display(Name = "CC Approval Code")]
    public string CreditCardApprovalCode { get; set; }

    [Display(Name = "Sub Total")]
    public decimal SubTotal { get; set; }

    [Display(Name = "Tax")]
    public decimal TaxAmt { get; set; }

    public decimal Freight { get; set; }

    [Display(Name = "Total Due")]
    public decimal TotalDue { get; set; }

    public string Comment { get; set; }

    [Display(Name = "Customer")]
    public string CustomerName { get; set; }

    public List<OrderLineItem> LineItems { get; set; } = new();

    [Display(Name = "Bill To")]
    public OrderAddress BillToAddress { get; set; }

    [Display(Name = "Ship To")]
    public OrderAddress ShipToAddress { get; set; }

    [Display(Name = "Modified Date")]
    public DateTime ModifiedDate { get; set; }
}

/// <summary>
/// Embedded line item within a SalesOrder document.
/// </summary>
public class OrderLineItem
{
    public int SalesOrderDetailId { get; set; }
    public int ProductId { get; set; }

    [Display(Name = "Product")]
    public string ProductName { get; set; }

    [Display(Name = "Qty")]
    public short OrderQty { get; set; }

    [Display(Name = "Unit Price")]
    public decimal UnitPrice { get; set; }

    [Display(Name = "Discount")]
    public decimal UnitPriceDiscount { get; set; }

    [Display(Name = "Line Total")]
    public decimal LineTotal { get; set; }
}

/// <summary>
/// Embedded address snapshot within a SalesOrder document.
/// </summary>
public class OrderAddress
{
    [Display(Name = "Address Line 1")]
    public string AddressLine1 { get; set; }

    [Display(Name = "Address Line 2")]
    public string AddressLine2 { get; set; }

    public string City { get; set; }

    [Display(Name = "State/Province")]
    public string StateProvince { get; set; }

    [Display(Name = "Country/Region")]
    public string CountryRegion { get; set; }

    [Display(Name = "Postal Code")]
    public string PostalCode { get; set; }
}
