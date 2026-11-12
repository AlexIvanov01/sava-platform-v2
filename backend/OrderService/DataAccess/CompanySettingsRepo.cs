using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OrderService.Models;

namespace OrderService.DataAccess;

public class CompanySettingsRepo(OrderContext context) : ICompanySettingsRepo
{
    public async Task<CompanySettings?> GetAsync() =>
        await context.CompanySettings.AsNoTracking().FirstOrDefaultAsync();

    public async Task<CompanySettings> SaveAsync(CompanySettings changes)
    {
        var settings = await context.CompanySettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new CompanySettings { Id = Guid.NewGuid() };
            context.CompanySettings.Add(settings);
        }

        settings.Name = changes.Name ?? settings.Name;
        settings.UIC = changes.UIC ?? settings.UIC;
        settings.VATNumber = changes.VATNumber ?? settings.VATNumber;
        settings.Address = changes.Address ?? settings.Address;
        settings.City = changes.City ?? settings.City;
        settings.Country = changes.Country ?? settings.Country;
        settings.Manager = changes.Manager ?? settings.Manager;
        settings.BankName = changes.BankName ?? settings.BankName;
        settings.IBAN = changes.IBAN ?? settings.IBAN;
        settings.BIC = changes.BIC ?? settings.BIC;
        settings.Email = changes.Email ?? settings.Email;
        settings.PhoneNumber = changes.PhoneNumber ?? settings.PhoneNumber;

        await context.SaveChangesAsync();
        return settings;
    }
}
