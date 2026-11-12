using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using AuthenticationService.Models;

namespace AuthenticationService.Dtos;

public record AuditReadDto(
    DateTime? CreatedAt,
    DateTime? UpdatedAt,
    string? CreatedBy,
    string? UpdatedBy
);

public record LoginRequestDto([Required] string? UserName, [Required] string? Password);

// Lifetimes are in seconds.
public record TokenResponseDto(
    string UserName,
    string Role,
    string AccessToken,
    int AccessTokenExpiresIn,
    string RefreshToken,
    int RefreshTokenExpiresIn
);

public record RefreshRequestDto([Required] string? RefreshToken);

public record ChangePasswordDto(
    [Required] string? CurrentPassword,
    [Required] [MinLength(PasswordRules.MinimumLength)] string? NewPassword
);

public record UserReadDto(Guid Id, string UserName, string Role, AuditReadDto Audit);

public record UserCreateDto(
    [Required] [MaxLength(100)] string? UserName,
    [Required] [MinLength(PasswordRules.MinimumLength)] string? Password,
    [Required] [AllowedRole] string? Role
);

// Null fields are left unchanged. Setting a password here resets it without the current one (admin only).
public record UserUpdateDto(
    [AllowedRole] string? Role,
    [MinLength(PasswordRules.MinimumLength)] string? Password
);

public static class PasswordRules
{
    public const int MinimumLength = 8;
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class AllowedRoleAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) =>
        value is null || (value is string role && User.Roles.Contains(role));

    public override string FormatErrorMessage(string name) =>
        $"{name} must be one of: {string.Join(", ", User.Roles)}.";
}
