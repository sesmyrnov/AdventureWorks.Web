using Microsoft.Azure.Cosmos;

namespace AdventureWorks.Web.Models;

/// <summary>
/// Holds singleton Container references injected via DI.
/// </summary>
public record CosmosContainers(
    Container CustomerOrders,
    Container ProductCatalog);
