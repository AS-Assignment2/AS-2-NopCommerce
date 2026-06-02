using OsposAdapter.Configuration;
using OsposAdapter.Services;

var builder = Host.CreateApplicationBuilder(args);

var osposConfig = new OsposConfig
{
    Host = Environment.GetEnvironmentVariable("OSPOS_DB_HOST") ?? "ospos_mysql",
    Database = Environment.GetEnvironmentVariable("OSPOS_DB_NAME") ?? "ospos",
    User = Environment.GetEnvironmentVariable("OSPOS_DB_USER") ?? "root",
    Password = Environment.GetEnvironmentVariable("OSPOS_DB_PASSWORD") ?? "ospospass",
    Port = int.Parse(Environment.GetEnvironmentVariable("OSPOS_DB_PORT") ?? "3306")
};

var rabbitMqConfig = new RabbitMqConfig
{
    Host = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "rabbitmq",
    Port = int.Parse(Environment.GetEnvironmentVariable("RABBITMQ_PORT") ?? "5672"),
    Username = Environment.GetEnvironmentVariable("RABBITMQ_USERNAME") ?? "guest",
    Password = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") ?? "guest",
    Exchange = Environment.GetEnvironmentVariable("RABBITMQ_EXCHANGE") ?? "verdemart.events"
};

var adapterConfig = new AdapterConfig
{
    PollingIntervalSeconds = int.Parse(Environment.GetEnvironmentVariable("POLLING_INTERVAL_SECONDS") ?? "30"),
    IdempotencyDbPath = Environment.GetEnvironmentVariable("IDEMPOTENCY_DB_PATH") ?? "/app/data/idempotency.db"
};

builder.Services.AddSingleton(osposConfig);
builder.Services.AddSingleton(rabbitMqConfig);
builder.Services.AddSingleton(adapterConfig);
builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddSingleton<IdempotencyTracker>();
builder.Services.AddHostedService<OsposPollingService>();
builder.Services.AddHostedService<StockUpdateConsumer>();

var host = builder.Build();
host.Run();
