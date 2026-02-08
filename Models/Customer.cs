using System.ComponentModel.DataAnnotations;

namespace AdventureWorks.Web.Models;

/// <summary>
/// Customer document — stored in the "customers" container with partition key /customerId.
/// Embeds addresses from the former Address/CustomerAddress tables.
/// Id and CustomerId hold the same value (customerId as string).
/// </summary>
public class Customer
{
    public string Id { get; set; }

    /// <summary>Partition key — same value as Id.</summary>
    public string CustomerId { get; set; }

    public string DocType { get; set; } = "customer";

    [Display(Name = "Name Style")]
    public bool NameStyle { get; set; }

    public string Title { get; set; }

    [Required]
    [Display(Name = "First Name")]
    public string FirstName { get; set; }

    [Display(Name = "Middle Name")]
    public string MiddleName { get; set; }

    [Required]
    [Display(Name = "Last Name")]
    public string LastName { get; set; }

    public string Suffix { get; set; }

    [Display(Name = "Company")]
    public string CompanyName { get; set; }

    [Display(Name = "Sales Person")]
    public string SalesPerson { get; set; }

    [Display(Name = "Email")]
    public string EmailAddress { get; set; }

    public string Phone { get; set; }

    [Display(Name = "Password Hash")]
    public string PasswordHash { get; set; }

    [Display(Name = "Password Salt")]
    public string PasswordSalt { get; set; }

    public List<CustomerAddress> Addresses { get; set; } = new();

    [Display(Name = "Modified Date")]
    public DateTime ModifiedDate { get; set; }
}

/// <summary>
/// Embedded address within a Customer document.
/// </summary>
public class CustomerAddress
{
    [Display(Name = "Address Type")]
    public string AddressType { get; set; }

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
