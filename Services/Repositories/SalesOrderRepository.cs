using AdventureWorks.Web.Models.Cosmos;
using Microsoft.Azure.Cosmos;
using System.Net;

namespace AdventureWorks.Web.Services.Repositories;

public class SalesOrderRepository : ISalesOrderRepository
{
    private readonly Container _container;

    public SalesOrderRepository(ICosmosDbService cosmosDb)
    {
        _container = cosmosDb.CustomerOrdersContainer;
    }

    public async Task<(List<SalesOrderDocument> Items, string? ContinuationToken)>
        ListOrdersByCustomerAsync(int customerId,
            string? continuationToken = null, int pageSize = 50)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.type = 'salesOrder' AND c.customerId = @custId ORDER BY c.orderDate DESC")
            .WithParameter("@custId", customerId);

        var options = new QueryRequestOptions
        {
            MaxItemCount = pageSize,
            PartitionKey = new PartitionKey(customerId)
        };

        var iterator = _container.GetItemQueryIterator<SalesOrderDocument>(
            query, continuationToken, options);

        var results = new List<SalesOrderDocument>();
        string? nextToken = null;

        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
            nextToken = response.ContinuationToken;
        }

        return (results, nextToken);
    }

    public async Task<(List<SalesOrderDocument> Items, string? ContinuationToken)>
        ListAllOrdersAsync(string? continuationToken = null, int pageSize = 50)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.type = 'salesOrder' ORDER BY c.orderDate DESC");

        var options = new QueryRequestOptions { MaxItemCount = pageSize };
        var iterator = _container.GetItemQueryIterator<SalesOrderDocument>(
            query, continuationToken, options);

        var results = new List<SalesOrderDocument>();
        string? nextToken = null;

        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
            nextToken = response.ContinuationToken;
        }

        return (results, nextToken);
    }

    public async Task<SalesOrderDocument?> GetOrderAsync(int customerId, int salesOrderId)
    {
        try
        {
            var response = await _container.ReadItemAsync<SalesOrderDocument>(
                $"order-{salesOrderId}",
                new PartitionKey(customerId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task CreateOrderAsync(SalesOrderDocument order)
    {
        order.Id = $"order-{order.SalesOrderId}";
        order.Type = "salesOrder";
        order.SalesOrderNumber = $"SO{order.SalesOrderId}";
        order.Ttl = 63072000;
        order.ModifiedDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        foreach (var detail in order.Details)
        {
            detail.LineTotal = detail.UnitPrice * (1 - detail.UnitPriceDiscount) * detail.OrderQty;
        }

        order.TotalDue = order.SubTotal + order.TaxAmt + order.Freight;

        await _container.CreateItemAsync(order, new PartitionKey(order.CustomerId));
    }

    public async Task UpdateOrderAsync(SalesOrderDocument order)
    {
        order.ModifiedDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        var options = new ItemRequestOptions();
        if (!string.IsNullOrEmpty(order.ETag))
            options.IfMatchEtag = order.ETag;

        await _container.ReplaceItemAsync(
            order, order.Id, new PartitionKey(order.CustomerId), options);
    }

    public async Task DeleteOrderAsync(int customerId, int salesOrderId)
    {
        await _container.DeleteItemAsync<SalesOrderDocument>(
            $"order-{salesOrderId}",
            new PartitionKey(customerId));
    }
}
