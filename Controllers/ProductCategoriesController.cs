using AdventureWorks.Web.Models.Cosmos;
using AdventureWorks.Web.Services.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Azure.Cosmos;
using System.Net;

namespace AdventureWorks.Web.Controllers;

public class ProductCategoriesController : Controller
{
    private readonly IProductCatalogRepository _repo;

    public ProductCategoriesController(IProductCatalogRepository repo)
    {
        _repo = repo;
    }

    // GET: ProductCategories
    public async Task<IActionResult> Index()
    {
        var categories = await _repo.ListCategoriesAsync();
        return View(categories);
    }

    // GET: ProductCategories/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();
        var category = await _repo.GetCategoryAsync(id.Value);
        if (category == null) return NotFound();
        return View(category);
    }

    // GET: ProductCategories/Create
    public async Task<IActionResult> Create()
    {
        var categories = await _repo.ListCategoriesAsync();
        ViewData["ParentProductCategoryId"] = new SelectList(categories, "ProductCategoryId", "Name");
        return View();
    }

    // POST: ProductCategories/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("ProductCategoryId,ParentProductCategoryId,Name")] CategoryDocument category)
    {
        if (ModelState.IsValid)
        {
            await _repo.CreateCategoryAsync(category);
            return RedirectToAction(nameof(Index));
        }
        var categories = await _repo.ListCategoriesAsync();
        ViewData["ParentProductCategoryId"] = new SelectList(categories, "ProductCategoryId", "Name", category.ParentProductCategoryId);
        return View(category);
    }

    // GET: ProductCategories/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();
        var category = await _repo.GetCategoryAsync(id.Value);
        if (category == null) return NotFound();
        var categories = await _repo.ListCategoriesAsync();
        ViewData["ParentProductCategoryId"] = new SelectList(categories, "ProductCategoryId", "Name", category.ParentProductCategoryId);
        return View(category);
    }

    // POST: ProductCategories/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id,
        [Bind("ProductCategoryId,ParentProductCategoryId,Name")] CategoryDocument category)
    {
        if (id != category.ProductCategoryId) return NotFound();

        if (ModelState.IsValid)
        {
            try
            {
                var existing = await _repo.GetCategoryAsync(id);
                if (existing == null) return NotFound();

                category.Id = existing.Id;
                category.PartitionKey = existing.PartitionKey;
                category.ETag = existing.ETag;
                category.SchemaVersion = existing.SchemaVersion;

                await _repo.UpdateCategoryAsync(category);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                if (!await _repo.CategoryExistsAsync(category.ProductCategoryId))
                    return NotFound();
                throw;
            }
            return RedirectToAction(nameof(Index));
        }
        var categories = await _repo.ListCategoriesAsync();
        ViewData["ParentProductCategoryId"] = new SelectList(categories, "ProductCategoryId", "Name", category.ParentProductCategoryId);
        return View(category);
    }

    // GET: ProductCategories/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();
        var category = await _repo.GetCategoryAsync(id.Value);
        if (category == null) return NotFound();
        return View(category);
    }

    // POST: ProductCategories/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        await _repo.DeleteCategoryAsync(id);
        return RedirectToAction(nameof(Index));
    }
}
