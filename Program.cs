using Azure.Identity;
using Microsoft.Azure.Cosmos;
using AdventureWorks.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// ─── Cosmos DB ───────────────────────────────────────────────────────
var cosmosSection = builder.Configuration.GetSection("CosmosDb");
var endpoint = cosmosSection["Endpoint"];
var databaseName = cosmosSection["DatabaseName"];
var productsContainer = cosmosSection["ProductsContainerName"];
var customersContainer = cosmosSection["CustomersContainerName"];

var cosmosClient = new CosmosClient(endpoint, new DefaultAzureCredential(),
    new CosmosClientOptions
    {
        SerializerOptions = new CosmosSerializationOptions
        {
            PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
        }
    });

builder.Services.AddSingleton(cosmosClient);
builder.Services.AddSingleton(new CosmosDbService(
    cosmosClient, databaseName, productsContainer, customersContainer));

// ─── MVC ─────────────────────────────────────────────────────────────
builder.Services.AddControllersWithViews();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=ProductCategories}/{action=Index}/{id?}");

app.Run();
