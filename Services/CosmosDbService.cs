using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Fluent;

namespace AdventureWorks.Web.Services;

public class CosmosDbService : ICosmosDbService, IDisposable
{
    private readonly CosmosClient _client;
    private readonly Database _database;

    public CosmosDbService(IConfiguration configuration)
    {
        var endpoint = configuration["CosmosDb:Endpoint"]
            ?? throw new InvalidOperationException("CosmosDb:Endpoint not configured");
        var databaseName = configuration["CosmosDb:DatabaseName"]
            ?? throw new InvalidOperationException("CosmosDb:DatabaseName not configured");

        var credential = new DefaultAzureCredential();

        _client = new CosmosClientBuilder(endpoint, credential)
            .WithConnectionModeDirect()
            .WithSerializerOptions(new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
            })
            .WithApplicationName("AdventureWorks.Web")
            .Build();

        _database = _client.GetDatabase(databaseName);
    }

    public Container CustomerOrdersContainer =>
        _database.GetContainer("customer-orders");

    public Container ProductCatalogContainer =>
        _database.GetContainer("product-catalog");

    public void Dispose() => _client.Dispose();
}
