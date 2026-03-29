using AdventureWorks.Web.Models.Cosmos;
using AdventureWorks.Web.Services.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using System.Net;

namespace AdventureWorks.Web.Controllers;

public class CustomersController : Controller
{
    private readonly ICustomerRepository _repo;

    public CustomersController(ICustomerRepository repo)
    {
        _repo = repo;
    }

    // GET: Customers
    public async Task<IActionResult> Index(string? continuationToken)
    {
        var (items, nextToken) = await _repo.ListCustomersAsync(continuationToken);
        ViewData["ContinuationToken"] = nextToken;
        return View(items);
    }

    // GET: Customers/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();
        var customer = await _repo.GetCustomerAsync(id.Value);
        if (customer == null) return NotFound();
        return View(customer);
    }

    // GET: Customers/Create
    public IActionResult Create() => View();

    // POST: Customers/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("CustomerId,NameStyle,Title,FirstName,MiddleName,LastName,Suffix,CompanyName,SalesPerson,EmailAddress,Phone,PasswordHash,PasswordSalt")] CustomerDocument customer)
    {
        if (ModelState.IsValid)
        {
            await _repo.CreateCustomerAsync(customer);
            return RedirectToAction(nameof(Index));
        }
        return View(customer);
    }

    // GET: Customers/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();
        var customer = await _repo.GetCustomerAsync(id.Value);
        if (customer == null) return NotFound();
        return View(customer);
    }

    // POST: Customers/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id,
        [Bind("CustomerId,NameStyle,Title,FirstName,MiddleName,LastName,Suffix,CompanyName,SalesPerson,EmailAddress,Phone,PasswordHash,PasswordSalt")] CustomerDocument customer)
    {
        if (id != customer.CustomerId) return NotFound();

        if (ModelState.IsValid)
        {
            try
            {
                var existing = await _repo.GetCustomerAsync(id);
                if (existing == null) return NotFound();

                customer.Id = existing.Id;
                customer.ETag = existing.ETag;
                customer.Addresses = existing.Addresses;
                customer.Ttl = existing.Ttl;
                customer.SchemaVersion = existing.SchemaVersion;

                await _repo.UpdateCustomerAsync(customer);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                if (!await _repo.CustomerExistsAsync(customer.CustomerId))
                    return NotFound();
                throw;
            }
            return RedirectToAction(nameof(Index));
        }
        return View(customer);
    }

    // GET: Customers/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();
        var customer = await _repo.GetCustomerAsync(id.Value);
        if (customer == null) return NotFound();
        return View(customer);
    }

    // POST: Customers/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        await _repo.DeleteCustomerAsync(id);
        return RedirectToAction(nameof(Index));
    }
}
