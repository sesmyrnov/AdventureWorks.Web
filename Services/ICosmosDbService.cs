using Microsoft.Azure.Cosmos;

namespace AdventureWorks.Web.Services;

public interface ICosmosDbService
{
    Container CustomerOrdersContainer { get; }
    Container ProductCatalogContainer { get; }
}
