using AdventureWorks.Web.Models;

namespace AdventureWorks.Web.Services;

public interface ICustomerService
{
    Task<List<CustomerDocument>> GetAllAsync();
    Task<CustomerDocument?> GetByIdAsync(int customerId);
    Task<CustomerDocument> CreateAsync(CustomerDocument customer);
    Task<CustomerDocument> UpdateAsync(CustomerDocument customer);
    Task DeleteAsync(int customerId);
}
