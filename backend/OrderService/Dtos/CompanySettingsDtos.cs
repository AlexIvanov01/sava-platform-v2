using System.ComponentModel.DataAnnotations;

namespace OrderService.Dtos;

public record CompanySettingsReadDto(
    string? Name,
    string? UIC,
    string? VATNumber,
    string? Address,
    string? City,
    string? Country,
    string? Manager,
    string? BankName,
    string? IBAN,
    string? BIC,
    string? Email,
    string? PhoneNumber,
    int? PaymentTermsDays,
    AuditReadDto Audit
);

// Partial update: null fields are left unchanged. Saving the settings for the first time needs
// at least Name and UIC.
public record CompanySettingsUpdateDto(
    [MaxLength(200)] string? Name,
    [MaxLength(20)] string? UIC,
    [MaxLength(20)] string? VATNumber,
    [MaxLength(500)] string? Address,
    [MaxLength(100)] string? City,
    [MaxLength(100)] string? Country,
    [MaxLength(100)] string? Manager,
    [MaxLength(100)] string? BankName,
    [MaxLength(35)] string? IBAN,
    [MaxLength(15)] string? BIC,
    [MaxLength(100)] [EmailAddress] string? Email,
    [MaxLength(20)] string? PhoneNumber,
    // Days from the invoice date to its due date; 14 when not set.
    [Range(0, 365)] int? PaymentTermsDays
);
