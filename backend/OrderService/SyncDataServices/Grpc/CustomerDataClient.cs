using System;
using System.Collections.Generic;
using System.Linq;
using Grpc.Net.Client;
using CustomerService;
using Microsoft.Extensions.Configuration;
using OrderService.Mappings;
using OrderService.Models;
using Serilog;

namespace OrderService.SyncDataServices.Grpc;

public class CustomerDataClient : ICustomerDataClient
{
    private readonly IConfiguration _configuration;
    public CustomerDataClient(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public IEnumerable<Customer>? ReturnAllCustomers()
    {
        Log.Information("--> Calling customer GRPC service {Service}", _configuration["GrpcCustomerService"]);

        var channel = GrpcChannel.ForAddress(_configuration["GrpcCustomerService"]
            ?? throw new InvalidOperationException("GrpcCustomerService is not configured."));
        var client = new GrpcCustomer.GrpcCustomerClient(channel);
        var request = new GetAllRequest();

        try
        {
            var reply = client.GetAllCustomers(request);
            return reply.Customers.Select(m => m.ToEntity()).ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not call GRPC server : {Ex}", ex.Message);
            return null;
        }
    }
}
