using System.ComponentModel.DataAnnotations;

namespace AdventureWorks.Web.Models;

/// <summary>
/// ProductModel document — stored in the "products" container with partition key /id.
/// Embeds descriptions (culture + description text) from the former ProductDescription/PMPDC tables.
/// </summary>
public class ProductModel
{
    public string Id { get; set; }

    public string DocType { get; set; } = "productModel";

    [Required]
    public string Name { get; set; }

    [Display(Name = "Catalog Description")]
    public string CatalogDescription { get; set; }

    public List<ProductModelDescription> Descriptions { get; set; } = new();

    [Display(Name = "Modified Date")]
    public DateTime ModifiedDate { get; set; }
}

/// <summary>
/// Embedded type for product model descriptions (culture + description text).
/// </summary>
public class ProductModelDescription
{
    public string Culture { get; set; }
    public string Description { get; set; }
}
