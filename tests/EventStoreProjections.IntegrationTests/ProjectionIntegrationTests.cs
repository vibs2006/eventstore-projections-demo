using System.Text.Json;
using EventStore.Client;
using EventStoreProjections.Models;
using EventStoreProjections.Repositories;
using EventStoreProjections.Services;
using EventStoreProjections.IntegrationTests.TestFixtures;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EventStoreProjections.IntegrationTests;

public class ProjectionIntegrationTests : IClassFixture<EventStoreTestFixture>
{
    private readonly EventStoreTestFixture _fixture;

    public ProjectionIntegrationTests(EventStoreTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Should_Project_OrderCreated_Event_To_ReadModel()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var orderCreated = new OrderCreatedEvent(
            orderId,
            "John Doe",
            100.50m,
            DateTime.UtcNow);

        var eventData = new EventData(
            Uuid.NewUuid(),
            "OrderCreated",
            JsonSerializer.SerializeToUtf8Bytes(orderCreated));

        var repository = new OrderReadModelRepository(_fixture.PostgresConnectionString);
        var checkpointRepo = new CheckpointRepository(_fixture.PostgresConnectionString);

        // Start projection service
        var service = new OrderProjectionService(
            _fixture.EventStoreClient,
            checkpointRepo,
            repository,
            NullLogger<OrderProjectionService>.Instance);

        var cts = new CancellationTokenSource();
        var serviceTask = service.StartAsync(cts.Token);

        // Act
        await _fixture.EventStoreClient.AppendToStreamAsync(
            $"order-{orderId}",
            StreamState.NoStream,
            new[] { eventData });

        // Wait for projection to process
        await Task.Delay(2000);

        // Stop service
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        var order = await repository.GetOrderAsync(orderId);
        order.Should().NotBeNull();
        order!.OrderId.Should().Be(orderId);
        order.CustomerName.Should().Be("John Doe");
        order.Amount.Should().Be(100.50m);
        order.Status.Should().Be("Created");
    }

    [Fact]
    public async Task Should_Update_Order_Status_When_OrderCompleted_Event_Occurs()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;
        var completedAt = createdAt.AddHours(1);

        var orderCreated = new OrderCreatedEvent(orderId, "Jane Smith", 250.75m, createdAt);
        var orderCompleted = new OrderCompletedEvent(orderId, completedAt);

        var events = new[]
        {
            new EventData(Uuid.NewUuid(), "OrderCreated", JsonSerializer.SerializeToUtf8Bytes(orderCreated)),
            new EventData(Uuid.NewUuid(), "OrderCompleted", JsonSerializer.SerializeToUtf8Bytes(orderCompleted))
        };

        var repository = new OrderReadModelRepository(_fixture.PostgresConnectionString);
        var checkpointRepo = new CheckpointRepository(_fixture.PostgresConnectionString);

        var service = new OrderProjectionService(
            _fixture.EventStoreClient,
            checkpointRepo,
            repository,
            NullLogger<OrderProjectionService>.Instance);

        var cts = new CancellationTokenSource();
        var serviceTask = service.StartAsync(cts.Token);

        // Act
        await _fixture.EventStoreClient.AppendToStreamAsync(
            $"order-{orderId}",
            StreamState.NoStream,
            events);

        await Task.Delay(2000);

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        var order = await repository.GetOrderAsync(orderId);
        order.Should().NotBeNull();
        order!.Status.Should().Be("Completed");
        order.CompletedAt.Should().BeCloseTo(completedAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Should_Save_And_Retrieve_Checkpoint()
    {
        // Arrange
        var checkpointRepo = new CheckpointRepository(_fixture.PostgresConnectionString);
        var projectionName = "TestProjection";
        ulong position = 12345;

        // Act
        await checkpointRepo.SaveCheckpointAsync(projectionName, position);
        var retrievedPosition = await checkpointRepo.GetCheckpointAsync(projectionName);

        // Assert
        retrievedPosition.Should().Be(position);
    }

    [Fact]
    public async Task Should_Resume_From_Checkpoint_After_Restart()
    {
        // Arrange
        var orderId1 = Guid.NewGuid();
        var orderId2 = Guid.NewGuid();

        var order1Created = new OrderCreatedEvent(orderId1, "Customer 1", 100m, DateTime.UtcNow);
        var order2Created = new OrderCreatedEvent(orderId2, "Customer 2", 200m, DateTime.UtcNow);

        var repository = new OrderReadModelRepository(_fixture.PostgresConnectionString);
        var checkpointRepo = new CheckpointRepository(_fixture.PostgresConnectionString);

        // First projection service - process first event
        var service1 = new OrderProjectionService(
            _fixture.EventStoreClient,
            checkpointRepo,
            repository,
            NullLogger<OrderProjectionService>.Instance);

        var cts1 = new CancellationTokenSource();
        var serviceTask1 = service1.StartAsync(cts1.Token);

        await _fixture.EventStoreClient.AppendToStreamAsync(
            $"order-{orderId1}",
            StreamState.NoStream,
            new[] { new EventData(Uuid.NewUuid(), "OrderCreated", JsonSerializer.SerializeToUtf8Bytes(order1Created)) });

        await Task.Delay(2000);
        cts1.Cancel();
        await service1.StopAsync(CancellationToken.None);

        // Second projection service - should resume and process second event
        var service2 = new OrderProjectionService(
            _fixture.EventStoreClient,
            checkpointRepo,
            repository,
            NullLogger<OrderProjectionService>.Instance);

        var cts2 = new CancellationTokenSource();
        var serviceTask2 = service2.StartAsync(cts2.Token);

        await _fixture.EventStoreClient.AppendToStreamAsync(
            $"order-{orderId2}",
            StreamState.NoStream,
            new[] { new EventData(Uuid.NewUuid(), "OrderCreated", JsonSerializer.SerializeToUtf8Bytes(order2Created)) });

        await Task.Delay(2000);
        cts2.Cancel();
        await service2.StopAsync(CancellationToken.None);

        // Assert - both orders should be in the read model
        var order1 = await repository.GetOrderAsync(orderId1);
        var order2 = await repository.GetOrderAsync(orderId2);

        order1.Should().NotBeNull();
        order1!.CustomerName.Should().Be("Customer 1");

        order2.Should().NotBeNull();
        order2!.CustomerName.Should().Be("Customer 2");
    }

    [Fact]
    public async Task Should_Handle_OrderCancelled_Event()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;
        var cancelledAt = createdAt.AddMinutes(30);

        var orderCreated = new OrderCreatedEvent(orderId, "Bob Johnson", 150.00m, createdAt);
        var orderCancelled = new OrderCancelledEvent(orderId, "Customer requested cancellation", cancelledAt);

        var events = new[]
        {
            new EventData(Uuid.NewUuid(), "OrderCreated", JsonSerializer.SerializeToUtf8Bytes(orderCreated)),
            new EventData(Uuid.NewUuid(), "OrderCancelled", JsonSerializer.SerializeToUtf8Bytes(orderCancelled))
        };

        var repository = new OrderReadModelRepository(_fixture.PostgresConnectionString);
        var checkpointRepo = new CheckpointRepository(_fixture.PostgresConnectionString);

        var service = new OrderProjectionService(
            _fixture.EventStoreClient,
            checkpointRepo,
            repository,
            NullLogger<OrderProjectionService>.Instance);

        var cts = new CancellationTokenSource();
        var serviceTask = service.StartAsync(cts.Token);

        // Act
        await _fixture.EventStoreClient.AppendToStreamAsync(
            $"order-{orderId}",
            StreamState.NoStream,
            events);

        await Task.Delay(2000);

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        var order = await repository.GetOrderAsync(orderId);
        order.Should().NotBeNull();
        order!.Status.Should().Be("Cancelled");
        order.CancelledAt.Should().BeCloseTo(cancelledAt, TimeSpan.FromSeconds(1));
        order.CancellationReason.Should().Be("Customer requested cancellation");
    }
}
