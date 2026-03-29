using System.Net;
using Microsoft.Azure.Cosmos;
using AdventureWorks.Web.Models;

namespace AdventureWorks.Web.Services;

public class SalesOrderService : ISalesOrderService
{
    private readonly Container _container;

    public SalesOrderService(CosmosContainers containers)
    {
        _container = containers.CustomerOrders;
    }

    public async Task<List<SalesOrderDocument>> GetByCustomerIdAsync(int customerId)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.type = 'salesOrder' AND c.customerId = @cid ORDER BY c.orderDate DESC")
            .WithParameter("@cid", customerId);
        var options = new QueryRequestOptions { PartitionKey = new PartitionKey(customerId) };
        return await ExecuteQueryAsync<SalesOrderDocument>(query, options);
    }

    public async Task<SalesOrderDocument?> GetByIdAsync(int salesOrderId, int customerId)
    {
        try
        {
            var response = await _container.ReadItemAsync<SalesOrderDocument>(
                id: $"order-{salesOrderId}",
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

    public async Task<SalesOrderDocument> CreateAsync(SalesOrderDocument order)
    {
        order.AssignId();
        order.ModifiedDate = DateTime.UtcNow;
        order.ComputeDerivedFields();
        var response = await _container.CreateItemAsync(
            order, new PartitionKey(order.CustomerId));
        return response.Resource;
    }

    public async Task<SalesOrderDocument> UpdateAsync(SalesOrderDocument order)
    {
        order.ModifiedDate = DateTime.UtcNow;
        order.ComputeDerivedFields();
        var options = order.ETag is not null
            ? new ItemRequestOptions { IfMatchEtag = order.ETag }
            : null;
        var response = await _container.ReplaceItemAsync(
            order, order.Id,
            new PartitionKey(order.CustomerId), options);
        return response.Resource;
    }

    public async Task DeleteAsync(int salesOrderId, int customerId)
    {
        await _container.DeleteItemAsync<SalesOrderDocument>(
            id: $"order-{salesOrderId}",
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
