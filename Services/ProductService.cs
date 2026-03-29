using System.Net;
using Microsoft.Azure.Cosmos;
using AdventureWorks.Web.Models;

namespace AdventureWorks.Web.Services;

public class ProductService : IProductService
{
    private readonly Container _container;

    public ProductService(CosmosContainers containers)
    {
        _container = containers.ProductCatalog;
    }

    public async Task<List<ProductDocument>> GetAllAsync()
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.type = 'product'");
        return await ExecuteQueryAsync<ProductDocument>(query);
    }

    public async Task<ProductDocument?> GetByIdAsync(int productId, int? productCategoryId = null)
    {
        if (productCategoryId.HasValue)
        {
            try
            {
                var response = await _container.ReadItemAsync<ProductDocument>(
                    id: $"product-{productId}",
                    partitionKey: new PartitionKey(productCategoryId.Value));
                var doc = response.Resource;
                doc.ETag = response.ETag;
                return doc;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.type = 'product' AND c.productId = @pid")
            .WithParameter("@pid", productId);
        var results = await ExecuteQueryAsync<ProductDocument>(query);
        return results.FirstOrDefault();
    }

    public async Task<ProductDocument> CreateAsync(ProductDocument product)
    {
        product.AssignId();
        product.ModifiedDate = DateTime.UtcNow;
        await DenormalizeNamesAsync(product);
        var response = await _container.CreateItemAsync(
            product, new PartitionKey(product.ProductCategoryId));
        return response.Resource;
    }

    public async Task<ProductDocument> UpdateAsync(ProductDocument product)
    {
        product.ModifiedDate = DateTime.UtcNow;
        await DenormalizeNamesAsync(product);
        var options = product.ETag is not null
            ? new ItemRequestOptions { IfMatchEtag = product.ETag }
            : null;
        var response = await _container.ReplaceItemAsync(
            product, product.Id,
            new PartitionKey(product.ProductCategoryId), options);
        return response.Resource;
    }

    public async Task DeleteAsync(int productId, int productCategoryId)
    {
        await _container.DeleteItemAsync<ProductDocument>(
            id: $"product-{productId}",
            partitionKey: new PartitionKey(productCategoryId));
    }

    public async Task<List<ProductCategoryDocument>> GetCategoriesForDropdownAsync()
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.type = 'productCategory'");
        return await ExecuteQueryAsync<ProductCategoryDocument>(query);
    }

    public async Task<List<ProductModelDocument>> GetModelsForDropdownAsync()
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.type = 'productModel'");
        var options = new QueryRequestOptions { PartitionKey = new PartitionKey(0) };
        return await ExecuteQueryAsync<ProductModelDocument>(query, options);
    }

    private async Task DenormalizeNamesAsync(ProductDocument product)
    {
        try
        {
            var catResp = await _container.ReadItemAsync<ProductCategoryDocument>(
                $"category-{product.ProductCategoryId}",
                new PartitionKey(product.ProductCategoryId));
            product.CategoryName = catResp.Resource.Name;
            product.ParentCategoryName = catResp.Resource.ParentCategoryName;
        }
        catch (CosmosException) { }

        if (product.ProductModelId.HasValue)
        {
            try
            {
                var modelResp = await _container.ReadItemAsync<ProductModelDocument>(
                    $"model-{product.ProductModelId}",
                    new PartitionKey(0));
                product.ProductModelName = modelResp.Resource.Name;
            }
            catch (CosmosException) { }
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
