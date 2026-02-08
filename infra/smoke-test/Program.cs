using System;
using Azure.Identity;
using Microsoft.Azure.Cosmos;

var credential = new DefaultAzureCredential();
var client = new CosmosClient("https://ssm-cosmos-adventureworks01.documents.azure.com:443/", credential);
var db = client.GetDatabase("adventureworks");
var products = await db.GetContainer("products").ReadContainerAsync();
var customers = await db.GetContainer("customers").ReadContainerAsync();
Console.WriteLine($"Account:   {client.Endpoint}");
Console.WriteLine($"Database:  adventureworks");
Console.WriteLine($"Container: {products.Resource.Id}  (PK: {products.Resource.PartitionKeyPath})");
Console.WriteLine($"Container: {customers.Resource.Id}  (PK: {customers.Resource.PartitionKeyPath})");
Console.WriteLine($"Status:    ALL OK - Entra ID RBAC auth verified end-to-end");
