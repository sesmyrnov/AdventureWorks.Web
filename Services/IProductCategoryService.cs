using AdventureWorks.Web.Models;

namespace AdventureWorks.Web.Services;

public interface IProductCategoryService
{
    Task<List<ProductCategoryDocument>> GetAllAsync();
    Task<ProductCategoryDocument?> GetByIdAsync(int productCategoryId);
    Task<ProductCategoryDocument> CreateAsync(ProductCategoryDocument category);
    Task<ProductCategoryDocument> UpdateAsync(ProductCategoryDocument category);
    Task DeleteAsync(int productCategoryId);
    Task<List<ProductCategoryDocument>> GetParentCategoriesForDropdownAsync();
}
