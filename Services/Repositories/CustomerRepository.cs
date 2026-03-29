using AdventureWorks.Web.Models.Cosmos;
using Microsoft.Azure.Cosmos;
using System.Net;

namespace AdventureWorks.Web.Services.Repositories;

public class CustomerRepository : ICustomerRepository
{
    private readonly Container _container;

    public CustomerRepository(ICosmosDbService cosmosDb)
    {
        _container = cosmosDb.CustomerOrdersContainer;
    }

    public async Task<(List<CustomerDocument> Items, string? ContinuationToken)>
        ListCustomersAsync(string? continuationToken = null, int pageSize = 50)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.type = 'customer' ORDER BY c.lastName");

        var options = new QueryRequestOptions { MaxItemCount = pageSize };
        var iterator = _container.GetItemQueryIterator<CustomerDocument>(
            query, continuationToken, options);

        var results = new List<CustomerDocument>();
        string? nextToken = null;

        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
            nextToken = response.ContinuationToken;
        }

        return (results, nextToken);
    }

    public async Task<CustomerDocument?> GetCustomerAsync(int customerId)
    {
        try
        {
            var response = await _container.ReadItemAsync<CustomerDocument>(
                $"customer-{customerId}",
                new PartitionKey(customerId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task CreateCustomerAsync(CustomerDocument customer)
    {
        customer.Id = $"customer-{customer.CustomerId}";
        customer.Type = "customer";
        customer.Ttl = -1;
        customer.ModifiedDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        await _container.CreateItemAsync(customer, new PartitionKey(customer.CustomerId));
    }

    public async Task UpdateCustomerAsync(CustomerDocument customer)
    {
        customer.ModifiedDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        var options = new ItemRequestOptions();
        if (!string.IsNullOrEmpty(customer.ETag))
        {
            options.IfMatchEtag = customer.ETag;
        }

        await _container.ReplaceItemAsync(
            customer, customer.Id, new PartitionKey(customer.CustomerId), options);
    }

    public async Task DeleteCustomerAsync(int customerId)
    {
        await _container.DeleteItemAsync<CustomerDocument>(
            $"customer-{customerId}",
            new PartitionKey(customerId));
    }

    public async Task<bool> CustomerExistsAsync(int customerId)
    {
        return await GetCustomerAsync(customerId) != null;
    }
}
