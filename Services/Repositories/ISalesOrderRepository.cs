using AdventureWorks.Web.Models.Cosmos;

namespace AdventureWorks.Web.Services.Repositories;

public interface ISalesOrderRepository
{
    Task<(List<SalesOrderDocument> Items, string? ContinuationToken)> ListOrdersByCustomerAsync(
        int customerId, string? continuationToken = null, int pageSize = 50);
    Task<(List<SalesOrderDocument> Items, string? ContinuationToken)> ListAllOrdersAsync(
        string? continuationToken = null, int pageSize = 50);
    Task<SalesOrderDocument?> GetOrderAsync(int customerId, int salesOrderId);
    Task CreateOrderAsync(SalesOrderDocument order);
    Task UpdateOrderAsync(SalesOrderDocument order);
    Task DeleteOrderAsync(int customerId, int salesOrderId);
}
