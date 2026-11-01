using RetailBillingApp.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetailBillingApp.Services;

public interface ISaleService
{
    Task<List<Sale>> GetAllSalesAsync();
    Task<Sale?> GetSaleByIdAsync(int id);
    Task<bool> RecordSaleAsync(Sale sale);
    Task<bool> RecordPaymentAsync(int saleId, decimal amount);
    Task<List<Sale>> GetOutstandingSalesAsync();
    Task<decimal> GetTotalOutstandingAsync();
    Task<int> SaveChangesAsync();
}
