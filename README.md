# EventStore Projections Demo

A production-ready demonstration of building event projections from EventStore/KurrentDB to a PostgreSQL read model using .NET 8, Dapper, and Testcontainers for integration testing.

## Architecture Overview

```
┌─────────────────┐
│   EventStore    │
│   ($all stream) │
└────────┬────────┘
         │ Subscribe
         ▼
┌─────────────────────────────┐
│ OrderProjectionService      │
│ (Background Service)        │
│                             │
│ • Loads checkpoint          │
│ • Processes events          │
│ • Updates read model        │
│ • Saves checkpoints         │
└──────────┬──────────────────┘
           │
           ▼
┌──────────────────────┐
│    PostgreSQL        │
│                      │
│ • orders_read_model  │
│ • event_checkpoints  │
└──────────────────────┘
```

## Key Features

✅ **Event Sourcing with EventStore** - Subscribe to all events using EventStore's gRPC client  
✅ **PostgreSQL Read Model** - Fast querying with Dapper (lightweight ORM)  
✅ **Checkpoint Management** - Resume from last processed position after restart  
✅ **Background Service** - .NET 8 Worker Service for continuous event processing  
✅ **Integration Tests** - Testcontainers for isolated, reproducible testing  
✅ **Docker Compose** - One-command local development environment  
✅ **Production Ready** - Proper error handling, logging, and graceful shutdown  

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop)
- [Visual Studio 2022+](https://visualstudio.microsoft.com/) or [VS Code](https://code.visualstudio.com/)

## Getting Started

### 1. Clone the Repository

```bash
git clone https://github.com/vibs2006/eventstore-projections-demo.git
cd eventstore-projections-demo
```

### 2. Start Infrastructure with Docker Compose

```bash
docker-compose up -d
```

This starts:
- **EventStore** on ports 2113 (HTTP) and 1113 (TCP)
- **PostgreSQL** on port 5432

Verify services are running:
```bash
docker-compose ps
```

Access EventStore UI: http://localhost:2113

### 3. Build the Solution

```bash
dotnet build
```

### 4. Run the Projection Service

```bash
dotnet run --project src/EventStoreProjections/EventStoreProjections.csproj
```

The service will:
1. Connect to EventStore
2. Load the last checkpoint (if any)
3. Subscribe to all events
4. Project events to PostgreSQL read model
5. Save checkpoints every 100 events

### 5. Test the Projection

In a separate terminal, connect to EventStore and append some test events:

```bash
# Using EventStore HTTP API
curl -X POST http://localhost:2113/streams/order-123 \
  -H "Content-Type: application/vnd.eventstore.events+json" \
  -d '[{
    "eventId": "'"$(uuidgen)"'",
    "eventType": "OrderCreated",
    "data": {
      "orderId": "123e4567-e89b-12d3-a456-426614174000",
      "customerName": "John Doe",
      "amount": 99.99,
      "createdAt": "'"$(date -u +%Y-%m-%dT%H:%M:%S.000Z)"'"
    }
  }]'
```

Or use the EventStore .NET client to append events programmatically.

### 6. Query the Read Model

Connect to PostgreSQL and query the read model:

```bash
docker exec -it postgres-projections psql -U postgres -d projections

SELECT * FROM orders_read_model;
SELECT * FROM event_checkpoints;
```

## Running Tests

### Unit/Integration Tests

The project includes comprehensive integration tests using Testcontainers:

```bash
dotnet test
```

The tests cover:
1. ✅ Projecting OrderCreated events to read model
2. ✅ Updating order status on OrderCompleted events
3. ✅ Checkpoint save and retrieval
4. ✅ Service resuming from checkpoint after restart
5. ✅ Handling OrderCancelled events

**Note:** Tests automatically spin up isolated EventStore and PostgreSQL containers, so they don't interfere with your local development environment.

### Test Output

```
Test run for EventStoreProjections.IntegrationTests.dll (.NET 8.0)
  
✓ Should_Project_OrderCreated_Event_To_ReadModel (3.2s)
✓ Should_Update_Order_Status_When_OrderCompleted_Event_Occurs (2.8s)
✓ Should_Save_And_Retrieve_Checkpoint (0.5s)
✓ Should_Resume_From_Checkpoint_After_Restart (4.1s)
✓ Should_Handle_OrderCancelled_Event (2.9s)

Test Run Successful.
Total tests: 5
     Passed: 5
```

## Database Schema

### orders_read_model

| Column | Type | Description |
|--------|------|-------------|
| order_id | UUID | Primary key, unique order identifier |
| customer_name | VARCHAR(255) | Customer's name |
| amount | DECIMAL(18,2) | Order amount |
| status | VARCHAR(50) | Order status (Created, Completed, Cancelled) |
| created_at | TIMESTAMP | Order creation timestamp |
| completed_at | TIMESTAMP | Order completion timestamp (nullable) |
| cancelled_at | TIMESTAMP | Order cancellation timestamp (nullable) |
| cancellation_reason | TEXT | Reason for cancellation (nullable) |

**Indexes:**
- `idx_orders_status` - Fast filtering by status
- `idx_orders_created_at` - Efficient sorting by creation date
- `idx_orders_customer_name` - Quick customer lookups

### event_checkpoints

| Column | Type | Description |
|--------|------|-------------|
| projection_name | VARCHAR(255) | Primary key, projection identifier |
| checkpoint_position | BIGINT | Last processed event position |
| last_updated | TIMESTAMP | Last checkpoint save timestamp |

## Project Structure

```
eventstore-projections-demo/
├── src/
│   └── EventStoreProjections/          # Main projection service
│       ├── Models/
│       │   ├── Events.cs               # Event definitions
│       │   └── OrderReadModel.cs       # Read model
│       ├── Repositories/
│       │   ├── CheckpointRepository.cs # Checkpoint persistence
│       │   └── OrderReadModelRepository.cs # Read model persistence
│       ├── Services/
│       │   └── OrderProjectionService.cs # Core projection logic
│       ├── Program.cs                  # Host configuration
│       └── appsettings.json           # Configuration
├── tests/
│   └── EventStoreProjections.IntegrationTests/
│       ├── TestFixtures/
│       │   └── EventStoreTestFixture.cs # Testcontainers setup
│       └── ProjectionIntegrationTests.cs # Integration tests
├── database/
│   └── init.sql                        # Database schema
├── docker-compose.yml                  # Local infrastructure
└── README.md
```

## Key Concepts

### Why Checkpoints?

Checkpoints enable **exactly-once processing** and **fault tolerance**:
- Store the position of the last successfully processed event
- On restart, resume from the checkpoint instead of replaying all events
- Save checkpoints periodically (every 100 events in this demo)
- Guarantee no events are lost or processed twice

### Why PostgreSQL + Dapper?

- **Fast queries**: Relational database optimized for read-heavy workloads
- **Familiar SQL**: Standard SQL queries, joins, and aggregations
- **Lightweight**: Dapper provides minimal overhead over raw ADO.NET
- **Flexible**: Easy to add indexes, views, and stored procedures

### Event Types

| Event | Description | Effect on Read Model |
|-------|-------------|---------------------|
| `OrderCreated` | New order placed | Create order with status "Created" |
| `OrderCompleted` | Order fulfilled | Update status to "Completed", set completed_at |
| `OrderCancelled` | Order cancelled | Update status to "Cancelled", set cancelled_at and reason |

## Development Guide

### Adding New Event Types

1. **Define the event** in `Models/Events.cs`:
   ```csharp
   public record OrderShippedEvent(Guid OrderId, string TrackingNumber, DateTime ShippedAt);
   ```

2. **Update the read model** in `Models/OrderReadModel.cs`:
   ```csharp
   public record OrderReadModel(
       // ... existing fields
       string? TrackingNumber = null,
       DateTime? ShippedAt = null
   );
   ```

3. **Add handler** in `OrderProjectionService.cs`:
   ```csharp
   case "OrderShipped":
       await HandleOrderShippedAsync(eventData, cancellationToken);
       break;
   ```

4. **Update database schema** in `database/init.sql`:
   ```sql
   ALTER TABLE orders_read_model 
   ADD COLUMN tracking_number VARCHAR(100) NULL,
   ADD COLUMN shipped_at TIMESTAMP NULL;
   ```

### Configuration

Environment variables (override appsettings.json):

```bash
export ConnectionStrings__EventStore="esdb://eventstore:2113?tls=false"
export ConnectionStrings__PostgreSQL="Host=postgres;Database=projections;Username=postgres;Password=postgres"
```

### Logging Levels

Adjust in `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "EventStoreProjections": "Debug"  // Set to "Information" in production
    }
  }
}
```

## Production Considerations

- [ ] **Use secrets management** - Never commit connection strings to source control
- [ ] **Enable TLS** - EventStore and PostgreSQL should use encrypted connections
- [ ] **Monitor checkpoints** - Alert if checkpoint age exceeds threshold
- [ ] **Scale projections** - Consider partitioning events for high-volume scenarios
- [ ] **Backup read model** - Regular PostgreSQL backups
- [ ] **Health checks** - Expose endpoints for Kubernetes/Docker health probes
- [ ] **Metrics** - Track projection lag, events/second, checkpoint frequency

## Troubleshooting

### Projection is not processing events

1. Check EventStore is running: `docker-compose ps`
2. Verify EventStore connection: http://localhost:2113
3. Check logs: `dotnet run --project src/EventStoreProjections`
4. Ensure PostgreSQL is accessible: `docker exec -it postgres-projections psql -U postgres`

### Tests are failing

1. Ensure Docker is running (Testcontainers requirement)
2. Check Docker has sufficient resources (4GB+ recommended)
3. Run tests with verbose output: `dotnet test --logger "console;verbosity=detailed"`

### Checkpoint not saving

1. Verify PostgreSQL connection string in `appsettings.json`
2. Check `event_checkpoints` table exists: `\dt` in psql
3. Review service logs for errors during checkpoint save

## Resources

- [EventStore Documentation](https://developers.eventstore.com/)
- [EventStore .NET Client](https://github.com/EventStore/EventStore-Client-Dotnet)
- [Dapper Documentation](https://github.com/DapperLib/Dapper)
- [Testcontainers for .NET](https://dotnet.testcontainers.org/)
- [Event Sourcing Patterns](https://martinfowler.com/eaaDev/EventSourcing.html)
- [CQRS Pattern](https://martinfowler.com/bliki/CQRS.html)

## License

MIT License - see LICENSE file for details

## Contributing

Contributions welcome! Please open an issue or pull request.

---

**Built with ❤️ using .NET 8, EventStore, PostgreSQL, and Dapper**
