using System;
using System.Collections.Generic;
using System.Linq;
using Grpc.Net.Client;
using InventoryService;
using Microsoft.Extensions.Configuration;
using OrderService.Mappings;
using OrderService.Models;
using Serilog;

namespace OrderService.SyncDataServices.Grpc;

public class InventoryDataClient : IInventoryDataClient
{
    private readonly IConfiguration _configuration;

    public InventoryDataClient(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public IEnumerable<Item>? ReturnAllItems()
    {
        Log.Information("--> Calling inventory GRPC service {Service}", _configuration["GrpcInventoryService"]);

        var channel = GrpcChannel.ForAddress(_configuration["GrpcInventoryService"]);
        var client = new GrpcInventory.GrpcInventoryClient(channel);
        var request = new GetAllRequest();

        try
        {
            var reply = client.GetAllItems(request);
            return reply.Item.Select(m => m.ToEntity()).ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not call GRPC server : {Ex}", ex.Message);
            return null;
        }
    }
}
