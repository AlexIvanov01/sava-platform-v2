using System;

namespace OrderService.SyncDataServices.Grpc;

// InventoryService or CustomerService didn't answer (in time). Controllers turn it into 503.
public class UpstreamUnavailableException(string service, Exception innerException)
    : Exception($"{service} is unavailable.", innerException)
{
    public string Service { get; } = service;
}
