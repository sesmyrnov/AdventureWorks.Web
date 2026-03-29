using AdventureWorks.Web.Models.Cosmos;
using AdventureWorks.Web.Services.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Azure.Cosmos;
using System.Net;

namespace AdventureWorks.Web.Controllers;

public class ProductsController : Controller
{
    private readonly IProductCatalogRepository _repo;

    public ProductsController(IProductCatalogRepository repo)
    {
        _repo = repo;
    }

    // GET: Products
    public async Task<IActionResult> Index(string? continuationToken)
    {
        var (items, nextToken) = await _repo.ListProductsAsync(continuationToken);
        ViewData["ContinuationToken"] = nextToken;
        return View(items);
    }

    // GET: Products/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();
        var product = await _repo.GetProductAsync(id.Value);
        if (product == null) return NotFound();
        return View(product);
    }

    // GET: Products/Create
    public async Task<IActionResult> Create()
    {
        var categories = await _repo.ListCategoriesAsync();
        var models = await _repo.ListModelsAsync();
        ViewData["ProductCategoryId"] = new SelectList(categories, "ProductCategoryId", "Name");
        ViewData["ProductModelId"] = new SelectList(models, "ProductModelId", "Name");
        return View();
    }

    // POST: Products/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("ProductId,Name,ProductNumber,Color,StandardCost,ListPrice,Size,Weight,ProductCategoryId,ProductModelId,SellStartDate,SellEndDate,DiscontinuedDate")] ProductDocument product)
    {
        if (ModelState.IsValid)
        {
            await _repo.CreateProductAsync(product);
            return RedirectToAction(nameof(Index));
        }
        var categories = await _repo.ListCategoriesAsync();
        var models = await _repo.ListModelsAsync();
        ViewData["ProductCategoryId"] = new SelectList(categories, "ProductCategoryId", "Name", product.ProductCategoryId);
        ViewData["ProductModelId"] = new SelectList(models, "ProductModelId", "Name", product.ProductModelId);
        return View(product);
    }

    // GET: Products/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();
        var product = await _repo.GetProductAsync(id.Value);
        if (product == null) return NotFound();
        var categories = await _repo.ListCategoriesAsync();
        var models = await _repo.ListModelsAsync();
        ViewData["ProductCategoryId"] = new SelectList(categories, "ProductCategoryId", "Name", product.ProductCategoryId);
        ViewData["ProductModelId"] = new SelectList(models, "ProductModelId", "Name", product.ProductModelId);
        return View(product);
    }

    // POST: Products/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id,
        [Bind("ProductId,Name,ProductNumber,Color,StandardCost,ListPrice,Size,Weight,ProductCategoryId,ProductModelId,SellStartDate,SellEndDate,DiscontinuedDate")] ProductDocument product)
    {
        if (id != product.ProductId) return NotFound();

        if (ModelState.IsValid)
        {
            try
            {
                var existing = await _repo.GetProductAsync(id);
                if (existing == null) return NotFound();

                product.Id = existing.Id;
                product.PartitionKey = existing.PartitionKey;
                product.ETag = existing.ETag;
                product.SchemaVersion = existing.SchemaVersion;

                await _repo.UpdateProductAsync(product);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                if (!await _repo.ProductExistsAsync(product.ProductId))
                    return NotFound();
                throw;
            }
            return RedirectToAction(nameof(Index));
        }
        var categories = await _repo.ListCategoriesAsync();
        var models = await _repo.ListModelsAsync();
        ViewData["ProductCategoryId"] = new SelectList(categories, "ProductCategoryId", "Name", product.ProductCategoryId);
        ViewData["ProductModelId"] = new SelectList(models, "ProductModelId", "Name", product.ProductModelId);
        return View(product);
    }

    // GET: Products/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();
        var product = await _repo.GetProductAsync(id.Value);
        if (product == null) return NotFound();
        return View(product);
    }

    // POST: Products/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        await _repo.DeleteProductAsync(id);
        return RedirectToAction(nameof(Index));
    }
}
