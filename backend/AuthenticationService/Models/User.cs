using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AuthenticationService.Models;

public class User : AuditableEntity
{
    public const string AdministratorRole = "Administrator";
    public const string UserRole = "User";
    public static readonly string[] Roles = [AdministratorRole, UserRole];

    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string UserName { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string PasswordHash { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Role { get; set; } = UserRole;

    public List<RefreshToken> RefreshTokens { get; set; } = [];
}
