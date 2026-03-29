using System.Net;
using Microsoft.Azure.Cosmos;
using AdventureWorks.Web.Models;

namespace AdventureWorks.Web.Services;

public class ProductCategoryService : IProductCategoryService
{
    private readonly Container _container;

    public ProductCategoryService(CosmosContainers containers)
    {
        _container = containers.ProductCatalog;
    }

    public async Task<List<ProductCategoryDocument>> GetAllAsync()
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.type = 'productCategory'");
        return await ExecuteQueryAsync<ProductCategoryDocument>(query);
    }

    public async Task<ProductCategoryDocument?> GetByIdAsync(int productCategoryId)
    {
        try
        {
            var response = await _container.ReadItemAsync<ProductCategoryDocument>(
                id: $"category-{productCategoryId}",
                partitionKey: new PartitionKey(productCategoryId));
            var doc = response.Resource;
            doc.ETag = response.ETag;
            return doc;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<ProductCategoryDocument> CreateAsync(ProductCategoryDocument category)
    {
        category.AssignId();
        category.ModifiedDate = DateTime.UtcNow;

        if (category.ParentProductCategoryId.HasValue)
        {
            var parent = await GetByIdAsync(category.ParentProductCategoryId.Value);
            category.ParentCategoryName = parent?.Name;
        }

        var response = await _container.CreateItemAsync(
            category, new PartitionKey(category.ProductCategoryId));
        return response.Resource;
    }

    public async Task<ProductCategoryDocument> UpdateAsync(ProductCategoryDocument category)
    {
        var old = await GetByIdAsync(category.ProductCategoryId);
        bool nameChanged = old is not null && old.Name != category.Name;

        if (category.ParentProductCategoryId.HasValue)
        {
            var parent = await GetByIdAsync(category.ParentProductCategoryId.Value);
            category.ParentCategoryName = parent?.Name;
        }
        else
        {
            category.ParentCategoryName = null;
        }

        category.ModifiedDate = DateTime.UtcNow;
        var options = category.ETag is not null
            ? new ItemRequestOptions { IfMatchEtag = category.ETag }
            : null;
        await _container.ReplaceItemAsync(
            category, category.Id,
            new PartitionKey(category.ProductCategoryId), options);

        if (nameChanged)
            await CascadeRenameAsync(category);

        return category;
    }

    public async Task DeleteAsync(int productCategoryId)
    {
        await _container.DeleteItemAsync<ProductCategoryDocument>(
            id: $"category-{productCategoryId}",
            partitionKey: new PartitionKey(productCategoryId));
    }

    public async Task<List<ProductCategoryDocument>> GetParentCategoriesForDropdownAsync()
    {
        return await GetAllAsync();
    }

    private async Task CascadeRenameAsync(ProductCategoryDocument category)
    {
        // Update products in same category partition
        var productQuery = new QueryDefinition(
            "SELECT * FROM c WHERE c.type = 'product' AND c.productCategoryId = @catId")
            .WithParameter("@catId", category.ProductCategoryId);
        var productOpts = new QueryRequestOptions
        {
            PartitionKey = new PartitionKey(category.ProductCategoryId)
        };
        var products = await ExecuteQueryAsync<ProductDocument>(productQuery, productOpts);
        foreach (var p in products)
        {
            p.CategoryName = category.Name;
            p.ModifiedDate = DateTime.UtcNow;
            await _container.ReplaceItemAsync(p, p.Id,
                new PartitionKey(p.ProductCategoryId));
        }

        // Update child categories
        var childQuery = new QueryDefinition(
            "SELECT * FROM c WHERE c.type = 'productCategory' AND c.parentProductCategoryId = @catId")
            .WithParameter("@catId", category.ProductCategoryId);
        var children = await ExecuteQueryAsync<ProductCategoryDocument>(childQuery);
        foreach (var child in children)
        {
            child.ParentCategoryName = category.Name;
            child.ModifiedDate = DateTime.UtcNow;
            await _container.ReplaceItemAsync(child, child.Id,
                new PartitionKey(child.ProductCategoryId));
        }
    }

    private async Task<List<T>> ExecuteQueryAsync<T>(QueryDefinition query,
        QueryRequestOptions? options = null)
    {
        var results = new List<T>();
        using var iterator = _container.GetItemQueryIterator<T>(query, requestOptions: options);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }
}
