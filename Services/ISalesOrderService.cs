using AdventureWorks.Web.Models;

namespace AdventureWorks.Web.Services;

public interface ISalesOrderService
{
    Task<List<SalesOrderDocument>> GetByCustomerIdAsync(int customerId);
    Task<SalesOrderDocument?> GetByIdAsync(int salesOrderId, int customerId);
    Task<SalesOrderDocument> CreateAsync(SalesOrderDocument order);
    Task<SalesOrderDocument> UpdateAsync(SalesOrderDocument order);
    Task DeleteAsync(int salesOrderId, int customerId);
}
