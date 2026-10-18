using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using OrderService.DataAccess;
using OrderService.Dtos;
using OrderService.Mappings;
using OrderService.Models;
using Serilog;

namespace OrderService.EventProcessing;

public class EventProcessor : IEventProcessor
{
    private readonly IServiceScopeFactory _scopeFactory;

    public EventProcessor(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task ProcessEvent(string message)
    {
        var eventType = DetermineEvent(message);

        switch (eventType)
        {
            case EventType.ItemPublished:
                await AddItem(message);
                break;
            case EventType.ItemUpdated:
                await UpdateItem(message);
                break;
            case EventType.ItemDeleted:
                await DeleteItem(message);
                break;
            case EventType.ProductNameUpdated:
                await UpdateProductName(message);
                break;
            case EventType.ProductDeleted:
                await DeleteProduct(message);
                break;
            case EventType.CustomerPublished:
                await AddCustomer(message);
                break;
            case EventType.CustomerUpdated:
                await UpdateCustomer(message);
                break;
            case EventType.CustomerDeleted:
                await DeleteCustomer(message);
                break;
            default:
                break;
        }
    }

    private async Task DeleteCustomer(string customerDeleteMessage)
    {
        Log.Information("--> Attemting to delete a customer...");

        try
        {
            using var scope = _scopeFactory.CreateScope();

            var repo = scope.ServiceProvider.GetRequiredService<ICustomerRepo>();

            var customerPublishedDto = JsonSerializer.Deserialize<CustomerPublishedDto>(
                customerDeleteMessage
            );

            if (customerPublishedDto == null || customerPublishedDto.ExternalId == Guid.Empty)
            {
                Log.Error(
                    "--> Could not delete customer because message was deserialized"
                        + " to null or external id was empty: {Dto}",
                    customerPublishedDto
                );
                return;
            }
            await repo.DeleteCustomerAsync(customerPublishedDto.ExternalId);
            Log.Information("Customer deleted: {Id}", customerPublishedDto.ExternalId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "--> Could not delete customer from DB {Message}", ex.Message);
        }
    }

    private async Task UpdateCustomer(string customerUpdatedMessage)
    {
        Log.Information("--> Attemting to update an customer...");

        try
        {
            using var scope = _scopeFactory.CreateScope();

            var repo = scope.ServiceProvider.GetRequiredService<ICustomerRepo>();

            var customerPublishedDto = JsonSerializer.Deserialize<CustomerPublishedDto>(
                customerUpdatedMessage
            );

            if (customerPublishedDto == null || customerPublishedDto.ExternalId == Guid.Empty)
            {
                Log.Error(
                    "--> Could not update customer because message was deserialized"
                        + " to null or external id was empty: {Dto}",
                    customerPublishedDto
                );
                return;
            }

            var customer = customerPublishedDto.ToEntity();

            await repo.UpdateCustomerAsync(customer);
            Log.Information("Customer updated: {Id}", customer.ExternalId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "--> Could not update customer in DB {Message}", ex.Message);
        }
    }

    private async Task AddCustomer(string customerPublishedMessage)
    {
        Log.Information("--> Attemting to add an item...");

        try
        {
            using var scope = _scopeFactory.CreateScope();

            var repo = scope.ServiceProvider.GetRequiredService<ICustomerRepo>();

            var customerPublishedDto = JsonSerializer.Deserialize<CustomerPublishedDto>(
                customerPublishedMessage
            );

            if (customerPublishedDto == null || customerPublishedDto.ExternalId == Guid.Empty)
            {
                Log.Error(
                    "--> Could not add customer because message was deserialized"
                        + " to null or external id was empty: {Dto}",
                    customerPublishedDto
                );
                return;
            }

            var customer = customerPublishedDto.ToEntity();

            var checkCustomer = await repo.ExternalCustomerExistsAsync(customer.ExternalId);

            if (!checkCustomer)
            {
                await repo.AddCustomerAsync(customer);
                Log.Information("--> Customer added!");
            }
            else
            {
                Log.Information("--> Customer already exists");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "--> Could not add customer to DB {Message}", ex.Message);
        }
    }

    private async Task DeleteProduct(string productNameUpdateMessage)
    {
        Log.Information("--> Attemting to delete items...");

        try
        {
            using var scope = _scopeFactory.CreateScope();

            var repo = scope.ServiceProvider.GetRequiredService<IItemRepo>();

            var itemPublishedDto = JsonSerializer.Deserialize<ItemPublishedDto>(
                productNameUpdateMessage
            );

            if (
                itemPublishedDto != null
                && itemPublishedDto.ExternalProductId != null
                && itemPublishedDto.ExternalProductId != Guid.Empty
            )
            {
                await repo.DeleteItemsByProductIdAsync(itemPublishedDto.ExternalProductId);
                Log.Information(
                    "--> Items deleted from database with product id {Id}",
                    itemPublishedDto.ExternalProductId
                );
            }
            else
            {
                Log.Error(
                    "--> Could not delete items because either mesage was deserialized"
                        + " to null or the external product id was not set: {Dto}",
                    itemPublishedDto
                );
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "--> Could not delete product name in DB {Message}", ex.Message);
        }
    }

    private async Task UpdateProductName(string productNameUpdateMessage)
    {
        Log.Information("--> Attemting to update item names...");

        try
        {
            using var scope = _scopeFactory.CreateScope();

            var repo = scope.ServiceProvider.GetRequiredService<IItemRepo>();

            var itemPublishedDto = JsonSerializer.Deserialize<ItemPublishedDto>(
                productNameUpdateMessage
            );

            if (
                itemPublishedDto != null
                && itemPublishedDto.ExternalProductId != null
                && itemPublishedDto.Name != null
            )
            {
                await repo.UpdateProductNameAsync(
                    itemPublishedDto.ExternalProductId,
                    itemPublishedDto.Name
                );

                Log.Information(
                    "--> Updated all item names with external" + " product id {Ext} with {Name}",
                    itemPublishedDto.ExternalProductId,
                    itemPublishedDto.Name
                );
            }
            else
            {
                Log.Error(
                    "--> Could not update product name because either mesage was deserialized"
                        + " to null or the external product id was not set or name field was not set: {Dto}",
                    itemPublishedDto
                );
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "--> Could not update product name in DB {Message}", ex.Message);
        }
    }

    private async Task DeleteItem(string itemDeletedMessage)
    {
        Log.Information("--> Attemting to delete an item...");

        try
        {
            using var scope = _scopeFactory.CreateScope();

            var repo = scope.ServiceProvider.GetRequiredService<IItemRepo>();

            var itemPublishedDto = JsonSerializer.Deserialize<ItemPublishedDto>(itemDeletedMessage);

            if (
                itemPublishedDto != null
                && itemPublishedDto.ExternalBatchId != null
                && itemPublishedDto.ExternalBatchId != Guid.Empty
            )
            {
                await repo.DeleteItemAsync(itemPublishedDto.ExternalBatchId);
                Log.Information(
                    "--> Item with id {Id} was deleted.",
                    itemPublishedDto.ExternalBatchId
                );
            }
            else
            {
                Log.Error(
                    "--> Could not delete item because either mesage was deserialized"
                        + " to null or the external batch id was not set: {Dto}",
                    itemPublishedDto
                );
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "--> Could not delete item from DB {Message}", ex.Message);
        }
    }

    private async Task UpdateItem(string itemUpdatedMessage)
    {
        Log.Information("--> Attemting to update an item...");

        try
        {
            using var scope = _scopeFactory.CreateScope();

            var repo = scope.ServiceProvider.GetRequiredService<IItemRepo>();

            var itemPublishedDto = JsonSerializer.Deserialize<ItemPublishedDto>(itemUpdatedMessage);

            var item = itemPublishedDto?.ToEntity();

            if (item != null && item.ExternalBatchId != Guid.Empty)
            {
                await repo.UpdateItemAsync(item);
                Log.Information("--> Item with id {Id} updated!", item.ExternalBatchId);
            }
            else
            {
                Log.Error(
                    "--> Could not update item because either mesage was deserialized"
                        + " to null or the external batch id was not set: {Dto}",
                    itemPublishedDto
                );
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "--> Could not update item in DB {Message}", ex.Message);
        }
    }

    private async Task AddItem(string itemPublishedMessage)
    {
        Log.Information("--> Attemting to add an item...");

        try
        {
            using var scope = _scopeFactory.CreateScope();

            var repo = scope.ServiceProvider.GetRequiredService<IItemRepo>();

            var itemPublishedDto = JsonSerializer.Deserialize<ItemPublishedDto>(
                itemPublishedMessage
            );

            if (
                itemPublishedDto == null
                || itemPublishedDto.ExternalBatchId == Guid.Empty
                || itemPublishedDto.ExternalProductId == Guid.Empty
                || itemPublishedDto.ExternalBatchId == null
                || itemPublishedDto.ExternalProductId == null
            )
            {
                Log.Error(
                    "--> Could not add item because message was deserialized"
                        + " to null or external id for batch or product was empty: {Dto}",
                    itemPublishedDto
                );
                return;
            }

            var item = itemPublishedDto.ToEntity();

            var checkItem = await repo.ExternalItemExistsAsync(item.ExternalBatchId);

            if (!checkItem)
            {
                await repo.AddItemAsync(item);
                Log.Information("--> Item added!");
            }
            else
            {
                Log.Information("--> Item already exists");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "--> Could not add item to DB {Message}", ex.Message);
        }
    }

    private static EventType DetermineEvent(string notificationMessage)
    {
        Log.Information("--> Message: {Message} \n--> Determining Event...", notificationMessage);

        var eventType = JsonSerializer.Deserialize<GenericEventDto>(notificationMessage);

        if (eventType == null)
        {
            Log.Error("--> Notification message deserialized to null.");
            return EventType.Undetermined;
        }

        switch (eventType.Event)
        {
            case "Item_Published":
                Log.Information("--> Item Published Event Detected");
                return EventType.ItemPublished;
            case "Item_Updated":
                Log.Information("--> Item Updated Event Detected");
                return EventType.ItemUpdated;
            case "Item_Deleted":
                Log.Information("--> Item Deleted Event Detected");
                return EventType.ItemDeleted;
            case "ProductName_Updated":
                Log.Information("--> Product Name Updated Event Detected");
                return EventType.ProductNameUpdated;
            case "Product_Deleted":
                Log.Information("--> Product Deleted Event Detected");
                return EventType.ProductDeleted;
            case "Customer_Published":
                Log.Information("--> Customer Published Event Detected");
                return EventType.CustomerPublished;
            case "Customer_Updated":
                Log.Information("--> Customer Updated Event Detected");
                return EventType.CustomerUpdated;
            case "Customer_Deleted":
                Log.Information("--> Customer Deleted Event Detected");
                return EventType.CustomerDeleted;
            default:
                Log.Information("--> Could not determine event type");
                return EventType.Undetermined;
        }
    }
}

enum EventType
{
    ItemPublished,
    ItemUpdated,
    ItemDeleted,
    ProductNameUpdated,
    ProductDeleted,
    CustomerPublished,
    CustomerDeleted,
    CustomerUpdated,
    Undetermined,
}
