using System;

namespace OrderService.Dtos;

public record AuditReadDto(
    DateTime? CreatedAt,
    DateTime? UpdatedAt,
    string? CreatedBy,
    string? UpdatedBy
);
