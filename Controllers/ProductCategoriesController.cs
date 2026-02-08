using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using AdventureWorks.Web.Models;
using AdventureWorks.Web.Services;

namespace AdventureWorks.Web.Controllers;

public class ProductCategoriesController : Controller
{
    private readonly CosmosDbService _cosmosDb;

    public ProductCategoriesController(CosmosDbService cosmosDb)
    {
        _cosmosDb = cosmosDb;
    }

    // GET: ProductCategories
    public async Task<IActionResult> Index()
    {
        var categories = await _cosmosDb.GetProductCategoriesAsync();
        return View(categories);
    }

    // GET: ProductCategories/Details/{id}
    public async Task<IActionResult> Details(string id)
    {
        if (id == null) return NotFound();

        var category = await _cosmosDb.GetProductCategoryAsync(id);
        if (category == null) return NotFound();

        return View(category);
    }

    // GET: ProductCategories/Create
    public async Task<IActionResult> Create()
    {
        await PopulateParentDropdown();
        return View();
    }

    // POST: ProductCategories/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("ParentProductCategoryId,Name")] ProductCategory category)
    {
        if (ModelState.IsValid)
        {
            category.Id = $"category-{Guid.NewGuid()}";
            category.DocType = "productCategory";
            category.ModifiedDate = DateTime.UtcNow;
            await DenormalizeParentName(category);
            await _cosmosDb.CreateProductCategoryAsync(category);
            return RedirectToAction(nameof(Index));
        }
        await PopulateParentDropdown(category);
        return View(category);
    }

    // GET: ProductCategories/Edit/{id}
    public async Task<IActionResult> Edit(string id)
    {
        if (id == null) return NotFound();

        var category = await _cosmosDb.GetProductCategoryAsync(id);
        if (category == null) return NotFound();

        await PopulateParentDropdown(category);
        return View(category);
    }

    // POST: ProductCategories/Edit/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id,
        [Bind("Id,ParentProductCategoryId,Name,ModifiedDate")] ProductCategory category)
    {
        if (id != category.Id) return NotFound();

        if (ModelState.IsValid)
        {
            category.DocType = "productCategory";
            category.ModifiedDate = DateTime.UtcNow;
            await DenormalizeParentName(category);

            if (!await _cosmosDb.ProductCategoryExistsAsync(id))
                return NotFound();

            await _cosmosDb.UpdateProductCategoryAsync(category);
            return RedirectToAction(nameof(Index));
        }
        await PopulateParentDropdown(category);
        return View(category);
    }

    // GET: ProductCategories/Delete/{id}
    public async Task<IActionResult> Delete(string id)
    {
        if (id == null) return NotFound();

        var category = await _cosmosDb.GetProductCategoryAsync(id);
        if (category == null) return NotFound();

        return View(category);
    }

    // POST: ProductCategories/Delete/{id}
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(string id)
    {
        await _cosmosDb.DeleteProductCategoryAsync(id);
        return RedirectToAction(nameof(Index));
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private async Task PopulateParentDropdown(ProductCategory current = null)
    {
        var categories = await _cosmosDb.GetProductCategoriesAsync();
        ViewData["ParentProductCategoryId"] = new SelectList(
            categories, "Id", "Name", current?.ParentProductCategoryId);
    }

    private async Task DenormalizeParentName(ProductCategory category)
    {
        if (!string.IsNullOrEmpty(category.ParentProductCategoryId))
        {
            var parent = await _cosmosDb.GetProductCategoryAsync(category.ParentProductCategoryId);
            category.ParentCategoryName = parent?.Name;
        }
        else
        {
            category.ParentCategoryName = null;
        }
    }
}
