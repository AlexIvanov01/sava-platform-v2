using System;
using System.Threading.Tasks;
using InventoryService.AsyncDataServices;
using InventoryService.DataAccess;
using InventoryService.Dtos;
using InventoryService.Mappings;
using InventoryService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace InventoryService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BatchesController : ControllerBase
    {
        private readonly IProductRepo _repository;
        private readonly IMessageBusClient _messageBusClient;
        private readonly TimeProvider _timeProvider;

        public BatchesController(
            IProductRepo repository,
            IMessageBusClient messageBusClient,
            TimeProvider timeProvider
        )
        {
            _repository = repository;
            _messageBusClient = messageBusClient;
            _timeProvider = timeProvider;
        }

        [HttpPost("{ProductId}")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ProductReadDto>> CreateBatch(
            BatchCreateDto batchCreateDto,
            Guid ProductId
        )
        {
            Product? productItem;
            ProductBatch batchModel;
            ProductReadDto productReadDto;

            try
            {
                Log.Information("--> Creating a batch............");

                batchModel = batchCreateDto.ToEntity();

                productItem = await _repository.AddBatchAsync(batchModel, ProductId);

                if (productItem == null)
                {
                    Log.Warning("--> Product with id {Id} not found to add batch to.", ProductId);
                    return NotFound();
                }

                Log.Information("--> Created batch for product: {Id}", productItem.Id);

                productReadDto = productItem.ToReadDto(
                    DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime)
                );
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
                return StatusCode(500, "An internal server error occurred.");
            }

            //Send Async Message
            try
            {
                ItemPublishedDto itemPublishedDto = new()
                {
                    ExternalBatchId = batchModel.Id,
                    ExternalProductId = ProductId,
                    Name = productItem.Name,
                    Lot = batchCreateDto.Lot,
                    Price = batchCreateDto.SellPrice,
                    ExpirationDate = batchCreateDto.ExpirationDate,
                    Event = "Item_Published",
                };
                await _messageBusClient.PublishNewItemAsync(itemPublishedDto);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error with publishing item message: {Message}", ex.Message);
            }

            return CreatedAtRoute(
                nameof(ProductsController.GetProductById),
                new { Id = productReadDto.Id },
                productReadDto
            );
        }

        [HttpPut("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<BatchReadDto>> UpdateBatch(
            Guid id,
            BatchUpdateDto productBatchDto
        )
        {
            ProductBatch savedBatch;
            try
            {
                Log.Information("--> Updating batch.............");

                var updated = await _repository.UpdateBatchAsync(productBatchDto.ToEntity(id));

                if (updated == null)
                {
                    Log.Warning("--> Batch with id {Id} not found for updating.", id);
                    return NotFound();
                }

                savedBatch = updated;

                Log.Information("--> Batch updated: {Id}", id);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
                return StatusCode(500, "An internal server error occurred.");
            }

            //Send Async Message
            if (
                productBatchDto.Lot != null
                || productBatchDto.SellPrice != null
                || productBatchDto.ExpirationDate != null
            )
            {
                try
                {
                    ItemPublishedDto itemPublishedDto = new()
                    {
                        ExternalBatchId = savedBatch.Id,
                        Lot = savedBatch.Lot,
                        Price = savedBatch.SellPrice,
                        ExpirationDate = savedBatch.ExpirationDate,
                        Event = "Item_Updated",
                    };

                    await _messageBusClient.PublishNewItemAsync(itemPublishedDto);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "--> Error with publishing item message: {Message}", ex.Message);
                }
            }

            return Ok(savedBatch.ToReadDto());
        }

        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<ActionResult> DeleteBatch(Guid id)
        {
            try
            {
                Log.Information("--> Deleting a batch...........");

                var (status, _) = await _repository.DeleteBatchAsync(id);
                if (status == DeleteStatus.NotFound)
                {
                    Log.Warning("--> Batch with id {Id} not found for deleting.", id);
                    return NotFound();
                }

                if (status == DeleteStatus.HasOrders)
                {
                    Log.Warning("--> Batch with id {Id} has orders; not deleted.", id);
                    return Problem(
                        title: "Batch has orders",
                        detail: $"Orders hold stock from batch {id}, so it can't be deleted.",
                        statusCode: StatusCodes.Status409Conflict
                    );
                }

                Log.Information("--> Batch with id {Id} deleted", id);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
                return StatusCode(500, "An internal server error occurred.");
            }

            //Send Async Message
            try
            {
                ItemPublishedDto itemPublishedDto = new()
                {
                    ExternalBatchId = id,
                    Event = "Item_Deleted",
                };

                await _messageBusClient.PublishNewItemAsync(itemPublishedDto);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error with publishing item message: {Message}", ex.Message);
            }

            return NoContent();
        }
    }
}
