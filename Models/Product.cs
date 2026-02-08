using System.ComponentModel.DataAnnotations;

namespace AdventureWorks.Web.Models;

/// <summary>
/// Product document — stored in the "products" container with partition key /id.
/// Denormalizes CategoryName, ParentCategoryName, and ModelName to avoid cross-document joins.
/// </summary>
public class Product
{
    public string Id { get; set; }

    public string DocType { get; set; } = "product";

    [Required]
    public string Name { get; set; }

    [Required]
    [Display(Name = "Product Number")]
    public string ProductNumber { get; set; }

    public string Color { get; set; }

    [Display(Name = "Standard Cost")]
    public decimal StandardCost { get; set; }

    [Display(Name = "List Price")]
    public decimal ListPrice { get; set; }

    public string Size { get; set; }

    public decimal? Weight { get; set; }

    [Display(Name = "Category")]
    public string ProductCategoryId { get; set; }

    [Display(Name = "Model")]
    public string ProductModelId { get; set; }

    [Display(Name = "Category")]
    public string CategoryName { get; set; }

    [Display(Name = "Parent Category")]
    public string ParentCategoryName { get; set; }

    [Display(Name = "Model")]
    public string ModelName { get; set; }

    [Display(Name = "Sell Start Date")]
    public DateTime SellStartDate { get; set; }

    [Display(Name = "Sell End Date")]
    public DateTime? SellEndDate { get; set; }

    [Display(Name = "Discontinued Date")]
    public DateTime? DiscontinuedDate { get; set; }

    [Display(Name = "Thumbnail File")]
    public string ThumbnailPhotoFileName { get; set; }

    [Display(Name = "Modified Date")]
    public DateTime ModifiedDate { get; set; }
}
