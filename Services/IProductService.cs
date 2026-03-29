using AdventureWorks.Web.Models;

namespace AdventureWorks.Web.Services;

public interface IProductService
{
    Task<List<ProductDocument>> GetAllAsync();
    Task<ProductDocument?> GetByIdAsync(int productId, int? productCategoryId = null);
    Task<ProductDocument> CreateAsync(ProductDocument product);
    Task<ProductDocument> UpdateAsync(ProductDocument product);
    Task DeleteAsync(int productId, int productCategoryId);
    Task<List<ProductCategoryDocument>> GetCategoriesForDropdownAsync();
    Task<List<ProductModelDocument>> GetModelsForDropdownAsync();
}
