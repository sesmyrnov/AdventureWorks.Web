using System.ComponentModel.DataAnnotations;

namespace AdventureWorks.Web.Models;

/// <summary>
/// ProductCategory document — stored in the "products" container with partition key /id.
/// Denormalizes ParentCategoryName to avoid cross-document joins.
/// </summary>
public class ProductCategory
{
    public string Id { get; set; }

    public string DocType { get; set; } = "productCategory";

    [Required]
    public string Name { get; set; }

    [Display(Name = "Parent Category")]
    public string ParentProductCategoryId { get; set; }

    [Display(Name = "Parent Category")]
    public string ParentCategoryName { get; set; }

    [Display(Name = "Modified Date")]
    public DateTime ModifiedDate { get; set; }
}
