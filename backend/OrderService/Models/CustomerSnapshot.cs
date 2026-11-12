using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OrderService.Models;

// Stored in the Orders table (owned by Order).
[Owned]
public class CustomerSnapshot
{
    [MaxLength(100)]
    public string? Name { get; set; }

    [MaxLength(200)]
    public string? CompanyName { get; set; }

    [MaxLength(100)]
    public string? Email { get; set; }

    [MaxLength(15)]
    public string? PhoneNumber { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(100)]
    public string? Country { get; set; }

    [MaxLength(100)]
    public string? BankName { get; set; }

    [MaxLength(35)]
    public string? IBAN { get; set; }

    [MaxLength(15)]
    public string? BIC { get; set; }

    [MaxLength(20)]
    public string? VATNumber { get; set; }

    [MaxLength(20)]
    public string? UIC { get; set; }
}
