# ADR-007: Synchronous HTTP + Asynchronous Messaging Communication Strategy

**Status:** Accepted

**Date:** 2026-01-14

**Context:** Microservices must communicate with each other to fulfill business operations. The communication pattern chosen impacts system coupling, resilience, consistency, and performance. Different operations have different requirements: some need immediate responses (synchronous), while others can be eventually consistent (asynchronous).

---

## Decision

Implement a **hybrid communication strategy**:

1. **Synchronous HTTP/REST** for request-response operations requiring immediate results
2. **Asynchronous Message Bus** (Azure Service Bus or RabbitMQ) for event-driven operations and eventual consistency

---

## Rationale

### Why Hybrid Approach?

**No single communication pattern fits all use cases:**

| Use Case | Pattern | Rationale |
|----------|---------|-----------|
| Get cart contents | Synchronous HTTP | User waits for response, immediate data needed |
| Check product price | Synchronous HTTP | Required for cart calculation, blocking operation |
| Reserve inventory | Synchronous HTTP | Part of checkout saga, need confirmation |
| Order created | Asynchronous Event | Notification, no immediate response needed |
| Send email confirmation | Asynchronous Event | Background task, user doesn't wait |
| Update analytics | Asynchronous Event | Non-critical, eventual consistency acceptable |

### Why Synchronous HTTP/REST?

**Use Cases:**
- Request-response operations (CRUD)
- Operations requiring immediate feedback (validation, queries)
- Saga orchestration steps (checkout workflow)

**Advantages:**
1. **Simple & Standard**
   - Well-understood REST semantics
   - Easy to debug (HTTP status codes, curl/Postman testing)
   - Browser/mobile native support

2. **Immediate Consistency**
   - Client knows immediately if operation succeeded
   - Easier error handling (return 4xx/5xx codes)

3. **Tooling & Ecosystem**
   - Extensive libraries (HttpClient, Polly)
   - API documentation (Swagger/OpenAPI)
   - Load balancing and routing mature

**Disadvantages:**
1. **Tight Coupling**
   - Caller blocked until callee responds
   - Cascading failures if downstream service unavailable

2. **Synchronous Overhead**
   - Caller thread waits (resource consumption)
   - Latency accumulates across service chain

**Mitigations:**
- Circuit breakers (Polly library) prevent cascading failures
- Timeouts limit wait time
- Retry with exponential backoff handles transient failures
- Bulkhead pattern isolates resource pools

### Why Asynchronous Messaging?

**Use Cases:**
- Event notifications (OrderCreated, PaymentProcessed, InventoryReserved)
- Background processing (email sending, analytics updates)
- Decoupling services (Order Service doesn't call Analytics Service directly)
- Fan-out scenarios (one event, multiple subscribers)

**Advantages:**
1. **Loose Coupling**
   - Publisher doesn't know subscribers (or if they exist)
   - Services can be added/removed without publisher changes
   - Temporal decoupling (publisher doesn't wait for subscriber)

2. **Scalability**
   - Messages queued, processed at subscriber's pace
   - Buffer for traffic spikes (queue absorbs load)
   - Easy to add more subscribers for parallel processing

3. **Resilience**
   - Publisher succeeds even if subscriber offline
   - Messages persisted until processed (durability)
   - Retry mechanisms built into message brokers

4. **Eventual Consistency**
   - Acceptable for non-critical operations
   - Enables distributed systems to scale

**Disadvantages:**
1. **Complexity**
   - Additional infrastructure (message broker)
   - More complex debugging (message flow tracing)
   - Message ordering challenges

2. **Eventual Consistency**
   - Subscribers process messages with delay
   - Data may be stale temporarily
   - Requires idempotency handling

3. **Operational Overhead**
   - Message broker must be highly available
   - Dead-letter queue monitoring
   - Message retention and purging policies

### Why Azure Service Bus / RabbitMQ?

**Criteria:**
- Reliability (message durability, at-least-once delivery)
- Publish-subscribe pattern support (topics/exchanges)
- Dead-letter queues for failed messages
- Cloud-native or self-hosted options

**Azure Service Bus (Recommended for Azure deployments):**
- Managed service (no broker management)
- Built-in duplicate detection
- Scheduled messages
- Sessions for ordered processing
- Integration with Azure ecosystem

**RabbitMQ (Alternative for cloud-agnostic or on-premises):**
- Open source, no vendor lock-in
- Rich routing patterns (direct, topic, fanout, headers)
- Strong community and plugin ecosystem
- Can run in Kubernetes (Bitnami Helm chart)

---

## Implementation Details

### 1. Synchronous HTTP Communication

#### Service-to-Service HTTP Client

**Pattern:** Typed HttpClient with Polly resilience policies

**Example: Cart Service calling Product Catalog Service**

```csharp
// Program.cs - Register HttpClient
builder.Services.AddHttpClient<IProductClient, ProductClient>(client =>
{
    client.BaseAddress = new Uri("http://product-catalog-service.retail-services.svc.cluster.local");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(10);
})
.AddTransientHttpErrorPolicy(policy => 
    policy.WaitAndRetryAsync(3, retryAttempt => 
        TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))))  // Exponential backoff
.AddTransientHttpErrorPolicy(policy => 
    policy.CircuitBreakerAsync(5, TimeSpan.FromSeconds(30)));  // Break after 5 failures

// ProductClient.cs - Implementation
public class ProductClient : IProductClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ProductClient> _logger;

    public ProductClient(HttpClient httpClient, ILogger<ProductClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<Product?> GetProductAsync(string sku, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/v1/products/{sku}", ct);
            
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;
            
            response.EnsureSuccessStatusCode();
            
            return await response.Content.ReadFromJsonAsync<Product>(cancellationToken: ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to get product {Sku} from Product Catalog Service", sku);
            throw;  // Circuit breaker will handle retries
        }
    }
}
```

#### Correlation ID Propagation

**Requirement:** Trace requests across service boundaries

**Implementation:**
```csharp
// Middleware to extract and propagate correlation ID
public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private const string CorrelationIdHeader = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[CorrelationIdHeader].FirstOrDefault()
                            ?? Guid.NewGuid().ToString("N");
        
        context.Items[CorrelationIdHeader] = correlationId;
        context.Response.Headers.Add(CorrelationIdHeader, correlationId);
        
        await _next(context);
    }
}

// HttpClient delegating handler to add correlation ID
public class CorrelationIdDelegatingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var correlationId = _httpContextAccessor.HttpContext?.Items["X-Correlation-ID"]?.ToString()
                            ?? Guid.NewGuid().ToString("N");
        
        request.Headers.Add("X-Correlation-ID", correlationId);
        
        return await base.SendAsync(request, cancellationToken);
    }
}
```

#### Service Discovery

**Kubernetes DNS:**
- Services accessible via DNS: `http://[service-name].[namespace].svc.cluster.local`
- Example: `http://cart-service.retail-services.svc.cluster.local`
- Kubernetes automatically load-balances across pod replicas

**No additional service discovery tool needed** (Consul, Eureka) in Kubernetes.

---

### 2. Asynchronous Messaging Communication

#### Event-Driven Pattern

**Event Definition:**
```csharp
// Shared.Events/OrderCreatedEvent.cs (shared library or copied to each service)
public record OrderCreatedEvent(
    int OrderId,
    string CustomerId,
    decimal Total,
    string Currency,
    List<OrderLineDto> Lines,
    DateTime CreatedUtc
);

public record OrderLineDto(string Sku, string Name, decimal UnitPrice, int Quantity);
```

#### Publisher (Checkout Service)

**Azure Service Bus Example:**
```csharp
// Program.cs - Register Service Bus client
builder.Services.AddSingleton(sp =>
{
    var connectionString = sp.GetRequiredService<IConfiguration>()
        .GetConnectionString("ServiceBus");
    return new ServiceBusClient(connectionString);
});

builder.Services.AddScoped<IEventPublisher, ServiceBusEventPublisher>();

// ServiceBusEventPublisher.cs
public class ServiceBusEventPublisher : IEventPublisher
{
    private readonly ServiceBusClient _client;
    private readonly ILogger<ServiceBusEventPublisher> _logger;

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
    {
        var topicName = typeof(TEvent).Name;  // "OrderCreatedEvent"
        var sender = _client.CreateSender(topicName);
        
        try
        {
            var json = JsonSerializer.Serialize(@event);
            var message = new ServiceBusMessage(json)
            {
                ContentType = "application/json",
                MessageId = Guid.NewGuid().ToString(),
                Subject = topicName
            };
            
            await sender.SendMessageAsync(message, ct);
            _logger.LogInformation("Published event {EventType} with ID {MessageId}", 
                topicName, message.MessageId);
        }
        finally
        {
            await sender.DisposeAsync();
        }
    }
}

// CheckoutService.cs - Publish event after order created
public async Task<Order> CheckoutAsync(string customerId, string paymentToken, CancellationToken ct)
{
    // ... checkout logic (reserve inventory, process payment, create order)
    
    var order = await _orderService.CreateOrderAsync(...);
    
    // Publish OrderCreatedEvent
    await _eventPublisher.PublishAsync(new OrderCreatedEvent(
        order.Id,
        order.CustomerId,
        order.Total,
        "GBP",
        order.Lines.Select(l => new OrderLineDto(l.Sku, l.Name, l.UnitPrice, l.Quantity)).ToList(),
        order.CreatedUtc
    ), ct);
    
    return order;
}
```

#### Subscriber (Analytics Service, Email Service)

**Azure Service Bus Subscriber:**
```csharp
// Program.cs - Register background service for event processing
builder.Services.AddHostedService<OrderCreatedEventHandler>();

// OrderCreatedEventHandler.cs (Analytics Service)
public class OrderCreatedEventHandler : BackgroundService
{
    private readonly ServiceBusClient _client;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OrderCreatedEventHandler> _logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var topicName = "OrderCreatedEvent";
        var subscriptionName = "analytics-service";
        
        var processor = _client.CreateProcessor(topicName, subscriptionName, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,  // Manual completion after processing
            MaxConcurrentCalls = 10,
            PrefetchCount = 20
        });
        
        processor.ProcessMessageAsync += ProcessMessageAsync;
        processor.ProcessErrorAsync += ProcessErrorAsync;
        
        await processor.StartProcessingAsync(stoppingToken);
        
        // Wait until cancellation
        await Task.Delay(Timeout.Infinite, stoppingToken);
        
        await processor.StopProcessingAsync();
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        try
        {
            var json = args.Message.Body.ToString();
            var @event = JsonSerializer.Deserialize<OrderCreatedEvent>(json);
            
            // Process event (update analytics database)
            using var scope = _serviceProvider.CreateScope();
            var analyticsRepo = scope.ServiceProvider.GetRequiredService<IAnalyticsRepository>();
            await analyticsRepo.RecordOrderAsync(@event.OrderId, @event.Total, @event.CreatedUtc);
            
            // Complete message (remove from queue)
            await args.CompleteMessageAsync(args.Message);
            
            _logger.LogInformation("Processed OrderCreatedEvent for Order {OrderId}", @event.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process OrderCreatedEvent: {MessageId}", args.Message.MessageId);
            
            // Dead-letter message after 3 retries
            if (args.Message.DeliveryCount >= 3)
                await args.DeadLetterMessageAsync(args.Message);
            else
                await args.AbandonMessageAsync(args.Message);  // Retry later
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Service Bus processor error: {ErrorSource}", args.ErrorSource);
        return Task.CompletedTask;
    }
}
```

#### Message Broker Configuration

**Azure Service Bus (Terraform example):**
```hcl
resource "azurerm_servicebus_namespace" "retail" {
  name                = "retail-servicebus"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = "Standard"  # Supports topics
}

resource "azurerm_servicebus_topic" "order_created" {
  name                = "OrderCreatedEvent"
  namespace_id        = azurerm_servicebus_namespace.retail.id
  enable_partitioning = true
  max_size_in_megabytes = 1024
}

resource "azurerm_servicebus_subscription" "analytics" {
  name                = "analytics-service"
  topic_id            = azurerm_servicebus_topic.order_created.id
  max_delivery_count  = 3  # Dead-letter after 3 retries
  lock_duration       = "PT5M"  # 5-minute lock
}

resource "azurerm_servicebus_subscription" "email" {
  name                = "email-service"
  topic_id            = azurerm_servicebus_topic.order_created.id
  max_delivery_count  = 10  # More retries for email (transient SMTP failures)
}
```

---

## Communication Patterns by Service

| Interaction | Pattern | Protocol | Reason |
|-------------|---------|----------|--------|
| **Cart → Product Catalog** (Get product) | Synchronous | HTTP GET | Immediate product data needed for cart validation |
| **Checkout → Cart** (Get cart) | Synchronous | HTTP GET | Required for checkout flow, blocking operation |
| **Checkout → Product Catalog** (Reserve inventory) | Synchronous | HTTP POST | Part of saga, need confirmation before payment |
| **Checkout → Payment Gateway** (Charge) | Synchronous | HTTP POST | Blocking operation, need immediate result |
| **Checkout → Order** (Create order) | Synchronous | HTTP POST | Part of saga, need order ID |
| **Checkout → Event Bus** (OrderCreated) | Asynchronous | Event | Notify subscribers after order created |
| **Order → Event Bus** (OrderShipped) | Asynchronous | Event | Status update, no immediate action needed |
| **Email Service ← Event Bus** (OrderCreated) | Asynchronous | Subscription | Background task, user doesn't wait |
| **Analytics ← Event Bus** (OrderCreated) | Asynchronous | Subscription | Eventual consistency acceptable |

---

## Consequences

### Positive

1. **Flexibility**
   - Right tool for right job (sync for immediate, async for eventual)
   - Services can evolve communication patterns independently

2. **Resilience**
   - Circuit breakers prevent cascading failures (HTTP)
   - Message queues buffer traffic spikes (async)
   - Retry mechanisms built-in

3. **Scalability**
   - Async messaging decouples services, enables independent scaling
   - Synchronous calls use HTTP/2 multiplexing for efficiency

4. **Observability**
   - Correlation IDs trace requests across sync calls
   - Message IDs trace events across async processing
   - Centralized logging and tracing

### Negative

1. **Complexity**
   - Two communication patterns to understand and maintain
   - Additional infrastructure (message broker)
   - More complex debugging (async message flows)

2. **Consistency Challenges**
   - Eventual consistency requires idempotency handling
   - Duplicate messages possible (at-least-once delivery)
   - Ordering not guaranteed unless explicitly configured

3. **Operational Overhead**
   - Message broker must be highly available
   - Dead-letter queue monitoring required
   - Synchronous calls need circuit breaker tuning

4. **Testing Difficulty**
   - Integration tests more complex (mock message bus)
   - End-to-end tests need to wait for async processing

---

## Alternatives Considered

### Alternative 1: Synchronous HTTP Only
- **Pros:** Simple, immediate consistency, easy debugging
- **Cons:** Tight coupling, cascading failures, poor scalability for fire-and-forget operations
- **Rejected:** Doesn't support event-driven patterns, poor resilience

### Alternative 2: Asynchronous Messaging Only
- **Pros:** Loose coupling, excellent scalability
- **Cons:** All operations eventual consistency (unacceptable for cart/checkout), latency for request-response
- **Rejected:** Users can't tolerate delay for cart operations

### Alternative 3: gRPC (instead of HTTP/REST)
- **Pros:** High performance (binary protocol), type-safe (protobuf), HTTP/2 streaming
- **Cons:** Less tooling/ecosystem than REST, harder debugging (binary), requires protobuf definition management
- **Deferred:** Consider for high-throughput internal services in future, REST sufficient for now

### Alternative 4: GraphQL (API Gateway)
- **Pros:** Flexible queries, reduce over-fetching, single endpoint
- **Cons:** Complexity, caching challenges, not suitable for commands (mutations)
- **Deferred:** Consider for BFF (Backend for Frontend) pattern in future

### Alternative 5: Apache Kafka (instead of Service Bus/RabbitMQ)
- **Pros:** High throughput, event sourcing support, log-based storage
- **Cons:** Operational complexity, overkill for current volume, requires ZooKeeper (or KRaft)
- **Rejected:** Over-engineered for current needs, Service Bus/RabbitMQ sufficient

---

## Testing Strategy

### Synchronous HTTP Testing

1. **Unit Tests:** Mock IProductClient with test responses
2. **Integration Tests:** Use TestServer to test HTTP endpoints
3. **Contract Tests:** Pact or similar to verify API contracts
4. **Chaos Tests:** Kill downstream service mid-request, validate circuit breaker

### Asynchronous Messaging Testing

1. **Unit Tests:** Mock IEventPublisher
2. **Integration Tests:** Use in-memory message broker (e.g., MassTransit's in-memory transport)
3. **End-to-End Tests:** Publish event, wait for processing, assert side effects
4. **Dead-Letter Tests:** Simulate processing failures, validate DLQ behavior

---

## Success Criteria

- ✅ Cart Service calls Product Catalog Service successfully (p95 < 100ms)
- ✅ Circuit breaker triggers after 5 failures, recovers after 30 seconds
- ✅ OrderCreated events delivered to all subscribers within 5 seconds
- ✅ Dead-letter queue contains unprocessable messages (not lost)
- ✅ Correlation IDs present in all logs (sync and async)
- ✅ Zero message loss during message broker restart (durability)

---

## References

- [Microservices Communication Patterns](https://microservices.io/patterns/communication-style/messaging.html)
- [Azure Service Bus Documentation](https://learn.microsoft.com/en-us/azure/service-bus-messaging/)
- [Polly Resilience Library](https://github.com/App-vNext/Polly)
- [Target-Architecture.md](./Target-Architecture.md) - Communication patterns section
- [Migration-Plan.md](./Migration-Plan.md) - Service extraction phases

---

**Review Cycle:** After each service extraction phase  
**Next Review:** After Phase 2 (Product Catalog Service extraction)
