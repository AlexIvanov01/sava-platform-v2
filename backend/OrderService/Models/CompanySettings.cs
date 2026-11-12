using System;
using System.ComponentModel.DataAnnotations;

namespace OrderService.Models;

// The company's own details, printed on its invoices as the supplier. There is at most one row.
public class CompanySettings : AuditableEntity
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(200)]
    public string? Name { get; set; }

    // ЕИК.
    [MaxLength(20)]
    public string? UIC { get; set; }

    [MaxLength(20)]
    public string? VATNumber { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(100)]
    public string? Country { get; set; }

    // Materially responsible person (МОЛ).
    [MaxLength(100)]
    public string? Manager { get; set; }

    [MaxLength(100)]
    public string? BankName { get; set; }

    [MaxLength(35)]
    public string? IBAN { get; set; }

    [MaxLength(15)]
    public string? BIC { get; set; }

    [MaxLength(100)]
    public string? Email { get; set; }

    [MaxLength(20)]
    public string? PhoneNumber { get; set; }

    // Days from the invoice date to its due date; 14 when not set.
    public int? PaymentTermsDays { get; set; }
}
