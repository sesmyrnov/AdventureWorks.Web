using Microsoft.AspNetCore.Mvc;
using AdventureWorks.Web.Models;
using AdventureWorks.Web.Services;

namespace AdventureWorks.Web.Controllers;

public class CustomersController : Controller
{
    private readonly CosmosDbService _cosmosDb;

    public CustomersController(CosmosDbService cosmosDb)
    {
        _cosmosDb = cosmosDb;
    }

    // GET: Customers
    public async Task<IActionResult> Index()
    {
        var customers = await _cosmosDb.GetCustomersAsync();
        return View(customers);
    }

    // GET: Customers/Details/{id}
    public async Task<IActionResult> Details(string id)
    {
        if (id == null) return NotFound();

        var customer = await _cosmosDb.GetCustomerAsync(id);
        if (customer == null) return NotFound();

        return View(customer);
    }

    // GET: Customers/Create
    public IActionResult Create()
    {
        return View();
    }

    // POST: Customers/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("NameStyle,Title,FirstName,MiddleName,LastName,Suffix," +
              "CompanyName,SalesPerson,EmailAddress,Phone,PasswordHash,PasswordSalt")] Customer customer)
    {
        if (ModelState.IsValid)
        {
            var newId = Guid.NewGuid().ToString();
            customer.Id = newId;
            customer.CustomerId = newId;
            customer.DocType = "customer";
            customer.ModifiedDate = DateTime.UtcNow;
            customer.Addresses ??= new List<CustomerAddress>();
            await _cosmosDb.CreateCustomerAsync(customer);
            return RedirectToAction(nameof(Index));
        }
        return View(customer);
    }

    // GET: Customers/Edit/{id}
    public async Task<IActionResult> Edit(string id)
    {
        if (id == null) return NotFound();

        var customer = await _cosmosDb.GetCustomerAsync(id);
        if (customer == null) return NotFound();

        return View(customer);
    }

    // POST: Customers/Edit/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id,
        [Bind("Id,CustomerId,NameStyle,Title,FirstName,MiddleName,LastName,Suffix," +
              "CompanyName,SalesPerson,EmailAddress,Phone,PasswordHash,PasswordSalt,ModifiedDate")] Customer customer)
    {
        if (id != customer.Id) return NotFound();

        if (ModelState.IsValid)
        {
            customer.DocType = "customer";
            customer.ModifiedDate = DateTime.UtcNow;

            if (!await _cosmosDb.CustomerExistsAsync(id))
                return NotFound();

            // Preserve existing addresses (not edited in this form)
            var existing = await _cosmosDb.GetCustomerAsync(id);
            customer.Addresses = existing?.Addresses ?? new List<CustomerAddress>();

            await _cosmosDb.UpdateCustomerAsync(customer);
            return RedirectToAction(nameof(Index));
        }
        return View(customer);
    }

    // GET: Customers/Delete/{id}
    public async Task<IActionResult> Delete(string id)
    {
        if (id == null) return NotFound();

        var customer = await _cosmosDb.GetCustomerAsync(id);
        if (customer == null) return NotFound();

        return View(customer);
    }

    // POST: Customers/Delete/{id}
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(string id)
    {
        await _cosmosDb.DeleteCustomerAsync(id);
        return RedirectToAction(nameof(Index));
    }
}
