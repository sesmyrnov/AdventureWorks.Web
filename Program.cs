using AdventureWorks.Web.Services;
using AdventureWorks.Web.Services.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Services
builder.Services.AddControllersWithViews();

// Cosmos DB — singleton CosmosClient
builder.Services.AddSingleton<ICosmosDbService, CosmosDbService>();

// Repositories — scoped
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<IProductCatalogRepository, ProductCatalogRepository>();
builder.Services.AddScoped<ISalesOrderRepository, SalesOrderRepository>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=ProductCategories}/{action=Index}/{id?}");

app.Run();
