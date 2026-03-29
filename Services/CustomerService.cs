using System.Net;
using Microsoft.Azure.Cosmos;
using AdventureWorks.Web.Models;

namespace AdventureWorks.Web.Services;

public class CustomerService : ICustomerService
{
    private readonly Container _container;

    public CustomerService(CosmosContainers containers)
    {
        _container = containers.CustomerOrders;
    }

    public async Task<List<CustomerDocument>> GetAllAsync()
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.type = 'customer'");
        return await ExecuteQueryAsync<CustomerDocument>(query);
    }

    public async Task<CustomerDocument?> GetByIdAsync(int customerId)
    {
        try
        {
            var response = await _container.ReadItemAsync<CustomerDocument>(
                id: $"customer-{customerId}",
                partitionKey: new PartitionKey(customerId));
            var doc = response.Resource;
            doc.ETag = response.ETag;
            return doc;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<CustomerDocument> CreateAsync(CustomerDocument customer)
    {
        customer.AssignId();
        customer.ModifiedDate = DateTime.UtcNow;
        var response = await _container.CreateItemAsync(
            customer, new PartitionKey(customer.CustomerId));
        return response.Resource;
    }

    public async Task<CustomerDocument> UpdateAsync(CustomerDocument customer)
    {
        customer.ModifiedDate = DateTime.UtcNow;
        var options = customer.ETag is not null
            ? new ItemRequestOptions { IfMatchEtag = customer.ETag }
            : null;
        var response = await _container.ReplaceItemAsync(
            customer, customer.Id,
            new PartitionKey(customer.CustomerId), options);
        return response.Resource;
    }

    public async Task DeleteAsync(int customerId)
    {
        await _container.DeleteItemAsync<CustomerDocument>(
            id: $"customer-{customerId}",
            partitionKey: new PartitionKey(customerId));
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
