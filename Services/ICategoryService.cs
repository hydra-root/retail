using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public interface ICategoryService
{
    Task<List<Category>> GetAllCategoriesAsync();
    ValueTask<Category?> GetCategoryByIdAsync(int id);
    Task<bool> AddCategoryAsync(Category category);
    Task<bool> UpdateCategoryAsync(Category category);
    Task<bool> DeleteCategoryAsync(int id);
}
