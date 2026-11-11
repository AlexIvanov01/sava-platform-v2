using System;
using System.ComponentModel.DataAnnotations;

namespace AuthenticationService.Models;

// Stamped by AuthContext on SaveChanges; see Auditing/AuditStamper.
public abstract class AuditableEntity
{
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    [MaxLength(100)]
    public string? CreatedBy { get; set; }

    [MaxLength(100)]
    public string? UpdatedBy { get; set; }
}
