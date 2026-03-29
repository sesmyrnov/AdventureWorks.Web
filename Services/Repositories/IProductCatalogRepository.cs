using AdventureWorks.Web.Models.Cosmos;

namespace AdventureWorks.Web.Services.Repositories;

public interface IProductCatalogRepository
{
    // Products
    Task<(List<ProductDocument> Items, string? ContinuationToken)> ListProductsAsync(
        string? continuationToken = null, int pageSize = 50);
    Task<ProductDocument?> GetProductAsync(int productId);
    Task CreateProductAsync(ProductDocument product);
    Task UpdateProductAsync(ProductDocument product);
    Task DeleteProductAsync(int productId);
    Task<bool> ProductExistsAsync(int productId);

    // Categories
    Task<List<CategoryDocument>> ListCategoriesAsync();
    Task<CategoryDocument?> GetCategoryAsync(int categoryId);
    Task CreateCategoryAsync(CategoryDocument category);
    Task UpdateCategoryAsync(CategoryDocument category);
    Task DeleteCategoryAsync(int categoryId);
    Task<bool> CategoryExistsAsync(int categoryId);

    // Models (for select lists)
    Task<List<ModelDocument>> ListModelsAsync();
}
