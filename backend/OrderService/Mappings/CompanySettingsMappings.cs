using OrderService.Dtos;
using OrderService.Models;

namespace OrderService.Mappings;

public static class CompanySettingsMappings
{
    public static CompanySettingsReadDto ToReadDto(this CompanySettings settings) =>
        new(
            settings.Name,
            settings.UIC,
            settings.VATNumber,
            settings.Address,
            settings.City,
            settings.Country,
            settings.Manager,
            settings.BankName,
            settings.IBAN,
            settings.BIC,
            settings.Email,
            settings.PhoneNumber,
            settings.ToAuditDto()
        );

    public static CompanySettings ToEntity(this CompanySettingsUpdateDto dto) =>
        new()
        {
            Name = dto.Name,
            UIC = dto.UIC,
            VATNumber = dto.VATNumber,
            Address = dto.Address,
            City = dto.City,
            Country = dto.Country,
            Manager = dto.Manager,
            BankName = dto.BankName,
            IBAN = dto.IBAN,
            BIC = dto.BIC,
            Email = dto.Email,
            PhoneNumber = dto.PhoneNumber,
        };
}
