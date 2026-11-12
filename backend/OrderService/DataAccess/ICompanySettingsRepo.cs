using System.Threading.Tasks;
using OrderService.Models;

namespace OrderService.DataAccess;

public interface ICompanySettingsRepo
{
    // Null until the settings have been saved once.
    Task<CompanySettings?> GetAsync();

    // Partial update of the single row, creating it when there is none yet; null fields are left
    // unchanged. Returns the saved settings.
    Task<CompanySettings> SaveAsync(CompanySettings changes);
}
