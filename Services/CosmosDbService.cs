using AdventureWorks.Web.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

namespace AdventureWorks.Web.Services;

/// <summary>
/// Data-access service for Azure Cosmos DB for NoSQL.
/// Uses two containers:
///   • products  (PK /id)       — Product, ProductCategory, ProductModel docs
///   • customers (PK /customerId) — Customer, SalesOrder docs
/// </summary>
public class CosmosDbService
{
    private readonly Container _products;
    private readonly Container _customers;

    public CosmosDbService(CosmosClient client, string databaseName,
        string productsContainer, string customersContainer)
    {
        var db = client.GetDatabase(databaseName);
        _products = db.GetContainer(productsContainer);
        _customers = db.GetContainer(customersContainer);
    }

    // ──────────────────────────── Products ────────────────────────────

    public async Task<IEnumerable<Product>> GetProductsAsync()
    {
        var query = _products.GetItemLinqQueryable<Product>()
            .Where(p => p.DocType == "product");

        using var iterator = query.ToFeedIterator();
        var results = new List<Product>();
        while (iterator.HasMoreResults)
        {
            results.AddRange(await iterator.ReadNextAsync());
        }
        return results;
    }

    public async Task<Product> GetProductAsync(string id)
    {
        try
        {
            var response = await _products.ReadItemAsync<Product>(id, new PartitionKey(id));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task CreateProductAsync(Product product)
    {
        await _products.CreateItemAsync(product, new PartitionKey(product.Id));
    }

    public async Task UpdateProductAsync(Product product)
    {
        await _products.ReplaceItemAsync(product, product.Id, new PartitionKey(product.Id));
    }

    public async Task DeleteProductAsync(string id)
    {
        await _products.DeleteItemAsync<Product>(id, new PartitionKey(id));
    }

    // ──────────────────────── Product Categories ──────────────────────

    public async Task<IEnumerable<ProductCategory>> GetProductCategoriesAsync()
    {
        var query = _products.GetItemLinqQueryable<ProductCategory>()
            .Where(c => c.DocType == "productCategory");

        using var iterator = query.ToFeedIterator();
        var results = new List<ProductCategory>();
        while (iterator.HasMoreResults)
        {
            results.AddRange(await iterator.ReadNextAsync());
        }
        return results;
    }

    public async Task<ProductCategory> GetProductCategoryAsync(string id)
    {
        try
        {
            var response = await _products.ReadItemAsync<ProductCategory>(id, new PartitionKey(id));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task CreateProductCategoryAsync(ProductCategory category)
    {
        await _products.CreateItemAsync(category, new PartitionKey(category.Id));
    }

    public async Task UpdateProductCategoryAsync(ProductCategory category)
    {
        await _products.ReplaceItemAsync(category, category.Id, new PartitionKey(category.Id));
    }

    public async Task DeleteProductCategoryAsync(string id)
    {
        await _products.DeleteItemAsync<ProductCategory>(id, new PartitionKey(id));
    }

    // ──────────────────────── Product Models ──────────────────────────

    public async Task<IEnumerable<ProductModel>> GetProductModelsAsync()
    {
        var query = _products.GetItemLinqQueryable<ProductModel>()
            .Where(m => m.DocType == "productModel");

        using var iterator = query.ToFeedIterator();
        var results = new List<ProductModel>();
        while (iterator.HasMoreResults)
        {
            results.AddRange(await iterator.ReadNextAsync());
        }
        return results;
    }

    public async Task<ProductModel> GetProductModelAsync(string id)
    {
        try
        {
            var response = await _products.ReadItemAsync<ProductModel>(id, new PartitionKey(id));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    // ──────────────────────────── Customers ───────────────────────────

    public async Task<IEnumerable<Customer>> GetCustomersAsync()
    {
        var query = _customers.GetItemLinqQueryable<Customer>()
            .Where(c => c.DocType == "customer");

        using var iterator = query.ToFeedIterator();
        var results = new List<Customer>();
        while (iterator.HasMoreResults)
        {
            results.AddRange(await iterator.ReadNextAsync());
        }
        return results;
    }

    public async Task<Customer> GetCustomerAsync(string id)
    {
        try
        {
            // For customers, id == customerId (partition key)
            var response = await _customers.ReadItemAsync<Customer>(id, new PartitionKey(id));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task CreateCustomerAsync(Customer customer)
    {
        await _customers.CreateItemAsync(customer, new PartitionKey(customer.CustomerId));
    }

    public async Task UpdateCustomerAsync(Customer customer)
    {
        await _customers.ReplaceItemAsync(customer, customer.Id, new PartitionKey(customer.CustomerId));
    }

    public async Task DeleteCustomerAsync(string id)
    {
        // id == customerId for customer documents
        await _customers.DeleteItemAsync<Customer>(id, new PartitionKey(id));
    }

    // ──────────────────────────── Sales Orders ───────────────────────

    public async Task<IEnumerable<SalesOrder>> GetSalesOrdersByCustomerAsync(string customerId)
    {
        var query = _customers.GetItemLinqQueryable<SalesOrder>(
                requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(customerId) })
            .Where(o => o.DocType == "salesOrder");

        using var iterator = query.ToFeedIterator();
        var results = new List<SalesOrder>();
        while (iterator.HasMoreResults)
        {
            results.AddRange(await iterator.ReadNextAsync());
        }
        return results;
    }

    public async Task<SalesOrder> GetSalesOrderAsync(string orderId, string customerId)
    {
        try
        {
            var response = await _customers.ReadItemAsync<SalesOrder>(orderId, new PartitionKey(customerId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    // ──────────────────────── Helpers ─────────────────────────────────

    /// <summary>
    /// Checks whether a document with the given id exists in the products container.
    /// </summary>
    public async Task<bool> ProductExistsAsync(string id)
    {
        return (await GetProductAsync(id)) != null;
    }

    /// <summary>
    /// Checks whether a product category exists.
    /// </summary>
    public async Task<bool> ProductCategoryExistsAsync(string id)
    {
        return (await GetProductCategoryAsync(id)) != null;
    }

    /// <summary>
    /// Checks whether a customer document exists.
    /// </summary>
    public async Task<bool> CustomerExistsAsync(string id)
    {
        return (await GetCustomerAsync(id)) != null;
    }
}
