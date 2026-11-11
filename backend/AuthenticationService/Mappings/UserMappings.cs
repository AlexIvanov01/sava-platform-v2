using System;
using AuthenticationService.Dtos;
using AuthenticationService.Models;

namespace AuthenticationService.Mappings;

public static class UserMappings
{
    public static UserReadDto ToReadDto(this User user) =>
        new(user.Id, user.UserName, user.Role, user.ToAuditDto());

    public static AuditReadDto ToAuditDto(this AuditableEntity entity) =>
        new(entity.CreatedAt, entity.UpdatedAt, entity.CreatedBy, entity.UpdatedBy);

    // The password is hashed by the caller; the DTO's Password is not copied.
    public static User ToEntity(this UserCreateDto dto, string passwordHash) =>
        new()
        {
            UserName = dto.UserName!,
            PasswordHash = passwordHash,
            Role = dto.Role!,
        };

    public static User ToEntity(this UserUpdateDto dto, Guid id, string? passwordHash) =>
        new()
        {
            Id = id,
            Role = dto.Role ?? string.Empty,
            PasswordHash = passwordHash ?? string.Empty,
        };
}
