using System;
using System.Threading.Tasks;
using CustomerService;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using OrderService.Mappings;
using OrderService.Models;
using Serilog;

namespace OrderService.SyncDataServices.Grpc;

// Singleton: one channel, reused for every call.
public sealed class CustomerDataClient : ICustomerDataClient, IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly GrpcCustomer.GrpcCustomerClient _client;
    private readonly TimeSpan _timeout;

    public CustomerDataClient(IConfiguration configuration)
    {
        _channel = GrpcChannel.ForAddress(
            configuration["GrpcCustomerService"]
                ?? throw new InvalidOperationException("GrpcCustomerService is not configured.")
        );
        _client = new GrpcCustomer.GrpcCustomerClient(_channel);
        _timeout = GrpcTimeout.From(configuration);
    }

    public async Task<CustomerSnapshot?> GetCustomerAsync(Guid id)
    {
        try
        {
            var reply = await _client.GetCustomerAsync(
                new GetCustomerRequest { Id = id.ToString() },
                deadline: DateTime.UtcNow.Add(_timeout)
            );
            return reply.ToSnapshot();
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
        catch (RpcException ex)
        {
            Log.Error(ex, "--> CustomerService GetCustomer failed: {Status}", ex.StatusCode);
            throw new UpstreamUnavailableException("CustomerService", ex);
        }
    }

    public void Dispose() => _channel.Dispose();
}
