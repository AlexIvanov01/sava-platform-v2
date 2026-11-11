using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AuthenticationService.Models;

// Only the SHA-256 hash of the token is stored; the raw value is returned to the client once.
public class RefreshToken
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(64)]
    [Column(TypeName = "varchar(64)")]
    public string TokenHash { get; set; } = string.Empty;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    // Set when the token was rotated; points at the token issued in its place.
    [MaxLength(64)]
    [Column(TypeName = "varchar(64)")]
    public string? ReplacedByTokenHash { get; set; }

    public bool IsActive(DateTime now) => RevokedAt == null && ExpiresAt > now;
}
