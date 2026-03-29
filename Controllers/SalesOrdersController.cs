using AdventureWorks.Web.Models.Cosmos;
using AdventureWorks.Web.Services.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using System.Net;

namespace AdventureWorks.Web.Controllers;

public class SalesOrdersController : Controller
{
    private readonly ISalesOrderRepository _orderRepo;
    private readonly ICustomerRepository _customerRepo;

    public SalesOrdersController(ISalesOrderRepository orderRepo, ICustomerRepository customerRepo)
    {
        _orderRepo = orderRepo;
        _customerRepo = customerRepo;
    }

    // GET: SalesOrders?customerId=29847
    public async Task<IActionResult> Index(int? customerId, string? continuationToken)
    {
        if (customerId.HasValue)
        {
            var customer = await _customerRepo.GetCustomerAsync(customerId.Value);
            if (customer == null) return NotFound();
            ViewData["Customer"] = customer;

            var (orders, nextToken) = await _orderRepo.ListOrdersByCustomerAsync(
                customerId.Value, continuationToken);
            ViewData["ContinuationToken"] = nextToken;
            return View(orders);
        }

        // List all orders
        var (allOrders, allNextToken) = await _orderRepo.ListAllOrdersAsync(continuationToken);
        ViewData["ContinuationToken"] = allNextToken;
        return View(allOrders);
    }

    // GET: SalesOrders/Details?customerId=29847&id=71774
    public async Task<IActionResult> Details(int customerId, int id)
    {
        var order = await _orderRepo.GetOrderAsync(customerId, id);
        if (order == null) return NotFound();
        return View(order);
    }

    // GET: SalesOrders/Delete?customerId=29847&id=71774
    public async Task<IActionResult> Delete(int customerId, int id)
    {
        var order = await _orderRepo.GetOrderAsync(customerId, id);
        if (order == null) return NotFound();
        return View(order);
    }

    // POST: SalesOrders/Delete
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int customerId, int id)
    {
        await _orderRepo.DeleteOrderAsync(customerId, id);
        return RedirectToAction(nameof(Index), new { customerId });
    }
}
