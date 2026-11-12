using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OrderService.Models;

// CompanySettings as they were when the invoice was created; stored in the Invoices table.
[Owned]
public class SupplierSnapshot
{
    [MaxLength(200)]
    public string? Name { get; set; }

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
}
