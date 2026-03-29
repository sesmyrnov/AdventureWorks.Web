using AdventureWorks.Web.Models.Cosmos;

namespace AdventureWorks.Web.Services.Repositories;

public interface ICustomerRepository
{
    Task<(List<CustomerDocument> Items, string? ContinuationToken)> ListCustomersAsync(
        string? continuationToken = null, int pageSize = 50);
    Task<CustomerDocument?> GetCustomerAsync(int customerId);
    Task CreateCustomerAsync(CustomerDocument customer);
    Task UpdateCustomerAsync(CustomerDocument customer);
    Task DeleteCustomerAsync(int customerId);
    Task<bool> CustomerExistsAsync(int customerId);
}
