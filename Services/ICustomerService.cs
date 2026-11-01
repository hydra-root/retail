using RetailBillingApp.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetailBillingApp.Services;

public interface ICustomerService
{
    Task<List<Customer>> GetAllCustomersAsync();
    ValueTask<Customer?> GetCustomerByIdAsync(int id);
    Task<bool> AddCustomerAsync(Customer customer);
    Task<bool> UpdateCustomerAsync(Customer customer);
    Task<bool> DeleteCustomerAsync(int id);
    Task<List<Customer>> GetCustomersWithDuesAsync();
    Task<int> SaveChangesAsync();
}
