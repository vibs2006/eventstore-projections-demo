using EventStore.Client;
using EventStoreProjections.Repositories;
using EventStoreProjections.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configure EventStore Client
var eventStoreSettings = EventStoreClientSettings.Create(
    builder.Configuration.GetConnectionString("EventStore") 
    ?? "esdb://localhost:2113?tls=false");

builder.Services.AddSingleton(new EventStoreClient(eventStoreSettings));

// Configure Repositories
var postgresConnectionString = builder.Configuration.GetConnectionString("PostgreSQL")
    ?? "Host=localhost;Database=projections;Username=postgres;Password=postgres";

builder.Services.AddSingleton<ICheckpointRepository>(
    new CheckpointRepository(postgresConnectionString));

builder.Services.AddSingleton<IOrderReadModelRepository>(
    new OrderReadModelRepository(postgresConnectionString));

// Add the projection service
builder.Services.AddHostedService<OrderProjectionService>();

var host = builder.Build();
host.Run();
