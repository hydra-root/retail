using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public class CustomerService : ICustomerService
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditService _auditService;

    public CustomerService(AppDbContext dbContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _auditService = auditService;
    }

    public Task<List<Customer>> GetAllCustomersAsync() => _dbContext.Customers.ToListAsync();
    public ValueTask<Customer?> GetCustomerByIdAsync(int id) => _dbContext.Customers.FindAsync(id);

    public async Task<bool> AddCustomerAsync(Customer customer)
    {
        try
        {
            customer.CreatedAt = System.DateTime.UtcNow;
            _dbContext.Customers.Add(customer);
            await _dbContext.SaveChangesAsync();
            await _auditService.RecordAsync("Create", "Customer", customer.Id, customer.Name, null, customer);
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> UpdateCustomerAsync(Customer customer)
    {
        try
        {
            var before = await _dbContext.Customers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == customer.Id);
            _dbContext.Customers.Update(customer);
            await _dbContext.SaveChangesAsync();
            await _auditService.RecordAsync("Update", "Customer", customer.Id, customer.Name, before, customer);
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> DeleteCustomerAsync(int id)
    {
        try
        {
            var customer = await _dbContext.Customers.FindAsync(id);
            if (customer == null) return false;
            _dbContext.Customers.Remove(customer);
            await _dbContext.SaveChangesAsync();
            await _auditService.RecordAsync("Delete", "Customer", id, customer.Name, customer, null);
            return true;
        }
        catch { return false; }
    }

    public async Task<List<Customer>> GetCustomersWithDuesAsync()
    {
        var pendingSales = await _dbContext.Sales
            .Where(s => s.PaidAmount < s.Amount)
            .Select(s => s.CustomerId)
            .Distinct()
            .ToListAsync();

        return await _dbContext.Customers
            .Where(c => pendingSales.Contains(c.Id))
            .ToListAsync();
    }

    public Task<int> SaveChangesAsync() => _dbContext.SaveChangesAsync();
}
