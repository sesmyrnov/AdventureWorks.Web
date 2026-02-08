using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using AdventureWorks.Web.Models;
using AdventureWorks.Web.Services;

namespace AdventureWorks.Web.Controllers;

public class ProductsController : Controller
{
    private readonly CosmosDbService _cosmosDb;

    public ProductsController(CosmosDbService cosmosDb)
    {
        _cosmosDb = cosmosDb;
    }

    // GET: Products
    public async Task<IActionResult> Index()
    {
        var products = await _cosmosDb.GetProductsAsync();
        return View(products);
    }

    // GET: Products/Details/{id}
    public async Task<IActionResult> Details(string id)
    {
        if (id == null) return NotFound();

        var product = await _cosmosDb.GetProductAsync(id);
        if (product == null) return NotFound();

        return View(product);
    }

    // GET: Products/Create
    public async Task<IActionResult> Create()
    {
        await PopulateDropdowns();
        return View();
    }

    // POST: Products/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("Name,ProductNumber,Color,StandardCost,ListPrice,Size,Weight," +
              "ProductCategoryId,ProductModelId,SellStartDate,SellEndDate," +
              "DiscontinuedDate,ThumbnailPhotoFileName")] Product product)
    {
        if (ModelState.IsValid)
        {
            product.Id = $"product-{Guid.NewGuid()}";
            product.DocType = "product";
            product.ModifiedDate = DateTime.UtcNow;
            await DenormalizeProductNames(product);
            await _cosmosDb.CreateProductAsync(product);
            return RedirectToAction(nameof(Index));
        }
        await PopulateDropdowns(product);
        return View(product);
    }

    // GET: Products/Edit/{id}
    public async Task<IActionResult> Edit(string id)
    {
        if (id == null) return NotFound();

        var product = await _cosmosDb.GetProductAsync(id);
        if (product == null) return NotFound();

        await PopulateDropdowns(product);
        return View(product);
    }

    // POST: Products/Edit/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id,
        [Bind("Id,Name,ProductNumber,Color,StandardCost,ListPrice,Size,Weight," +
              "ProductCategoryId,ProductModelId,SellStartDate,SellEndDate," +
              "DiscontinuedDate,ThumbnailPhotoFileName,ModifiedDate")] Product product)
    {
        if (id != product.Id) return NotFound();

        if (ModelState.IsValid)
        {
            product.DocType = "product";
            product.ModifiedDate = DateTime.UtcNow;
            await DenormalizeProductNames(product);

            if (!await _cosmosDb.ProductExistsAsync(id))
                return NotFound();

            await _cosmosDb.UpdateProductAsync(product);
            return RedirectToAction(nameof(Index));
        }
        await PopulateDropdowns(product);
        return View(product);
    }

    // GET: Products/Delete/{id}
    public async Task<IActionResult> Delete(string id)
    {
        if (id == null) return NotFound();

        var product = await _cosmosDb.GetProductAsync(id);
        if (product == null) return NotFound();

        return View(product);
    }

    // POST: Products/Delete/{id}
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(string id)
    {
        await _cosmosDb.DeleteProductAsync(id);
        return RedirectToAction(nameof(Index));
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private async Task PopulateDropdowns(Product product = null)
    {
        var categories = await _cosmosDb.GetProductCategoriesAsync();
        var models = await _cosmosDb.GetProductModelsAsync();

        ViewData["ProductCategoryId"] = new SelectList(
            categories, "Id", "Name", product?.ProductCategoryId);
        ViewData["ProductModelId"] = new SelectList(
            models, "Id", "Name", product?.ProductModelId);
    }

    /// <summary>
    /// Resolves the denormalized CategoryName, ParentCategoryName, and ModelName
    /// from the referenced category/model documents.
    /// </summary>
    private async Task DenormalizeProductNames(Product product)
    {
        if (!string.IsNullOrEmpty(product.ProductCategoryId))
        {
            var category = await _cosmosDb.GetProductCategoryAsync(product.ProductCategoryId);
            product.CategoryName = category?.Name;
            product.ParentCategoryName = category?.ParentCategoryName;
        }
        if (!string.IsNullOrEmpty(product.ProductModelId))
        {
            var model = await _cosmosDb.GetProductModelAsync(product.ProductModelId);
            product.ModelName = model?.Name;
        }
    }
}
