using AdventureWorks.Web.Models.Cosmos;
using Microsoft.Azure.Cosmos;
using System.Net;

namespace AdventureWorks.Web.Services.Repositories;

public class ProductCatalogRepository : IProductCatalogRepository
{
    private readonly Container _container;

    public ProductCatalogRepository(ICosmosDbService cosmosDb)
    {
        _container = cosmosDb.ProductCatalogContainer;
    }

    // --- Products ---

    public async Task<(List<ProductDocument> Items, string? ContinuationToken)>
        ListProductsAsync(string? continuationToken = null, int pageSize = 50)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.type = 'product' ORDER BY c.name");

        var options = new QueryRequestOptions { MaxItemCount = pageSize };
        var iterator = _container.GetItemQueryIterator<ProductDocument>(
            query, continuationToken, options);

        var results = new List<ProductDocument>();
        string? nextToken = null;

        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
            nextToken = response.ContinuationToken;
        }

        return (results, nextToken);
    }

    public async Task<ProductDocument?> GetProductAsync(int productId)
    {
        try
        {
            var pk = $"product-{productId}";
            var response = await _container.ReadItemAsync<ProductDocument>(pk, new PartitionKey(pk));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task CreateProductAsync(ProductDocument product)
    {
        product.Id = $"product-{product.ProductId}";
        product.PartitionKey = product.Id;
        product.Type = "product";
        product.ModifiedDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        if (product.ProductCategoryId.HasValue)
        {
            var cat = await GetCategoryAsync(product.ProductCategoryId.Value);
            if (cat != null)
                product.Category = new CategorySnapshot
                {
                    ProductCategoryId = cat.ProductCategoryId,
                    Name = cat.Name,
                    ParentCategoryName = cat.ParentCategoryName
                };
        }

        if (product.ProductModelId.HasValue)
        {
            var model = await GetModelAsync(product.ProductModelId.Value);
            if (model != null)
                product.Model = new ModelSnapshot
                {
                    ProductModelId = model.ProductModelId,
                    Name = model.Name
                };
        }

        await _container.CreateItemAsync(product, new PartitionKey(product.PartitionKey));
    }

    public async Task UpdateProductAsync(ProductDocument product)
    {
        product.ModifiedDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        if (product.ProductCategoryId.HasValue)
        {
            var cat = await GetCategoryAsync(product.ProductCategoryId.Value);
            product.Category = cat != null ? new CategorySnapshot
            {
                ProductCategoryId = cat.ProductCategoryId,
                Name = cat.Name,
                ParentCategoryName = cat.ParentCategoryName
            } : null;
        }
        else { product.Category = null; }

        if (product.ProductModelId.HasValue)
        {
            var model = await GetModelAsync(product.ProductModelId.Value);
            product.Model = model != null ? new ModelSnapshot
            {
                ProductModelId = model.ProductModelId,
                Name = model.Name
            } : null;
        }
        else { product.Model = null; }

        var options = new ItemRequestOptions();
        if (!string.IsNullOrEmpty(product.ETag))
            options.IfMatchEtag = product.ETag;

        await _container.ReplaceItemAsync(
            product, product.Id, new PartitionKey(product.PartitionKey), options);
    }

    public async Task DeleteProductAsync(int productId)
    {
        var pk = $"product-{productId}";
        await _container.DeleteItemAsync<ProductDocument>(pk, new PartitionKey(pk));
    }

    public async Task<bool> ProductExistsAsync(int productId)
    {
        return await GetProductAsync(productId) != null;
    }

    // --- Categories ---

    public async Task<List<CategoryDocument>> ListCategoriesAsync()
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.type = 'category' ORDER BY c.name");

        var iterator = _container.GetItemQueryIterator<CategoryDocument>(query);
        var results = new List<CategoryDocument>();

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }

        return results;
    }

    public async Task<CategoryDocument?> GetCategoryAsync(int categoryId)
    {
        try
        {
            var pk = $"category-{categoryId}";
            var response = await _container.ReadItemAsync<CategoryDocument>(pk, new PartitionKey(pk));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task CreateCategoryAsync(CategoryDocument category)
    {
        category.Id = $"category-{category.ProductCategoryId}";
        category.PartitionKey = category.Id;
        category.Type = "category";
        category.ModifiedDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        if (category.ParentProductCategoryId.HasValue)
        {
            var parent = await GetCategoryAsync(category.ParentProductCategoryId.Value);
            category.ParentCategoryName = parent?.Name;
        }

        await _container.CreateItemAsync(category, new PartitionKey(category.PartitionKey));
    }

    public async Task UpdateCategoryAsync(CategoryDocument category)
    {
        category.ModifiedDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        if (category.ParentProductCategoryId.HasValue)
        {
            var parent = await GetCategoryAsync(category.ParentProductCategoryId.Value);
            category.ParentCategoryName = parent?.Name;
        }
        else { category.ParentCategoryName = null; }

        var options = new ItemRequestOptions();
        if (!string.IsNullOrEmpty(category.ETag))
            options.IfMatchEtag = category.ETag;

        await _container.ReplaceItemAsync(
            category, category.Id, new PartitionKey(category.PartitionKey), options);

        // Cascade: update embedded category snapshots in products
        await CascadeCategoryRenameAsync(
            category.ProductCategoryId, category.Name, category.ParentCategoryName);
    }

    public async Task DeleteCategoryAsync(int categoryId)
    {
        var pk = $"category-{categoryId}";
        await _container.DeleteItemAsync<CategoryDocument>(pk, new PartitionKey(pk));
    }

    public async Task<bool> CategoryExistsAsync(int categoryId)
    {
        return await GetCategoryAsync(categoryId) != null;
    }

    // --- Models ---

    public async Task<List<ModelDocument>> ListModelsAsync()
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.type = 'model' ORDER BY c.name");

        var iterator = _container.GetItemQueryIterator<ModelDocument>(query);
        var results = new List<ModelDocument>();

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }

        return results;
    }

    private async Task<ModelDocument?> GetModelAsync(int modelId)
    {
        try
        {
            var pk = $"model-{modelId}";
            var response = await _container.ReadItemAsync<ModelDocument>(pk, new PartitionKey(pk));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    // --- Cascade ---

    private async Task CascadeCategoryRenameAsync(int categoryId, string newName, string? newParentName)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.type = 'product' AND c.productCategoryId = @catId")
            .WithParameter("@catId", categoryId);

        var iterator = _container.GetItemQueryIterator<ProductDocument>(query);

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            foreach (var product in response)
            {
                product.Category = new CategorySnapshot
                {
                    ProductCategoryId = categoryId,
                    Name = newName,
                    ParentCategoryName = newParentName
                };
                product.ModifiedDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

                await _container.ReplaceItemAsync(
                    product, product.Id, new PartitionKey(product.PartitionKey));
            }
        }
    }
}
