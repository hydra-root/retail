using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public class CategoryService : ICategoryService
{
    private readonly AppDbContext _dbContext;

    public CategoryService(AppDbContext dbContext) => _dbContext = dbContext;

    public Task<List<Category>> GetAllCategoriesAsync() =>
        _dbContext.Categories
            .Include(c => c.ParentCategory)
            .OrderBy(c => c.Name)
            .ToListAsync();

    public ValueTask<Category?> GetCategoryByIdAsync(int id) => _dbContext.Categories.FindAsync(id);

    public async Task<bool> AddCategoryAsync(Category category)
    {
        try
        {
            if (await _dbContext.Categories.AnyAsync(c => c.Name == category.Name))
                return false; // duplicate name

            _dbContext.Categories.Add(category);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> UpdateCategoryAsync(Category category)
    {
        try
        {
            var existing = await _dbContext.Categories.FindAsync(category.Id);
            if (existing == null) return false;

            if (await _dbContext.Categories.AnyAsync(c => c.Name == category.Name && c.Id != category.Id))
                return false; // duplicate name

            // A category cannot be its own ancestor
            if (category.ParentCategoryId == category.Id)
                return false;

            existing.Name = category.Name;
            existing.Description = category.Description;
            existing.ParentCategoryId = category.ParentCategoryId;
            existing.IsActive = category.IsActive;

            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> DeleteCategoryAsync(int id)
    {
        try
        {
            var category = await _dbContext.Categories.FindAsync(id);
            if (category == null) return false;

            // Block deletion when in use by products or child categories
            var inUseByProduct = await _dbContext.Products.AnyAsync(p => p.CategoryId == id);
            var hasChildren = await _dbContext.Categories.AnyAsync(c => c.ParentCategoryId == id);
            if (inUseByProduct || hasChildren)
                return false;

            _dbContext.Categories.Remove(category);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }
}
