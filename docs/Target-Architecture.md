# Target Architecture - Microservices Evolution

**Status:** Proposed  
**Date:** 2026-01-14  
**Version:** 1.0

## Executive Summary

This document outlines the target architecture for migrating the Retail Monolith from a single deployable unit to a microservices-based system using an incremental strangler pattern. The architecture prioritizes:

- **Container-based deployment** (Docker + Kubernetes)
- **Incremental migration** (no big-bang rewrite)
- **Behavior preservation** (existing functionality remains intact)
- **Clear service boundaries** (domain-driven design principles)

---

## 1. Target Service Boundaries

Based on analysis of the current monolith (HLD, LLD, existing ADRs), we propose decomposition into the following microservices:

### 1.1 Proposed Services

```
┌─────────────────────────────────────────────────────────┐
│                    API Gateway                          │
│              (Routing, Auth, Rate Limiting)             │
└─────────────────────────────────────────────────────────┘
           │              │              │           │
           ▼              ▼              ▼           ▼
    ┌──────────┐   ┌──────────┐   ┌──────────┐   ┌──────────┐
    │ Product  │   │   Cart   │   │ Checkout │   │  Order   │
    │ Catalog  │   │ Service  │   │ Service  │   │ Service  │
    │ Service  │   │          │   │          │   │          │
    └──────────┘   └──────────┘   └──────────┘   └──────────┘
         │              │               │              │
         ▼              ▼               ▼              ▼
    ┌──────────┐   ┌──────────┐   ┌──────────┐   ┌──────────┐
    │ Product  │   │   Cart   │   │ Payment  │   │  Order   │
    │    DB    │   │    DB    │   │ Gateway  │   │    DB    │
    └──────────┘   └──────────┘   └──────────┘   └──────────┘
                                        │
                                        ▼
                                   ┌──────────┐
                                   │Inventory │
                                   │    DB    │
                                   └──────────┘
```

#### **Service 1: Product Catalog Service**
- **Domain:** Product and Inventory Management
- **Responsibilities:**
  - Product information (SKU, name, description, price, category)
  - Inventory tracking and availability checks
  - Product catalog queries (active products, search, filtering)
- **Data Ownership:**
  - `Product` table
  - `InventoryItem` table
- **API Endpoints:**
  - `GET /api/v1/products` - List active products
  - `GET /api/v1/products/{id}` - Get product details
  - `GET /api/v1/products/{sku}/inventory` - Check inventory availability
  - `POST /api/v1/inventory/reserve` - Reserve inventory (internal)
  - `POST /api/v1/inventory/release` - Release reserved inventory (internal)
- **Key Characteristics:**
  - Read-heavy workload
  - High cacheable data
  - Infrequent writes

#### **Service 2: Cart Service**
- **Domain:** Shopping Cart Management
- **Responsibilities:**
  - Create and manage shopping carts
  - Add/remove cart items
  - Calculate cart totals
  - Cart persistence and retrieval
- **Data Ownership:**
  - `Cart` table
  - `CartLine` table
- **API Endpoints:**
  - `GET /api/v1/cart/{customerId}` - Get customer cart
  - `POST /api/v1/cart/{customerId}/items` - Add item to cart
  - `DELETE /api/v1/cart/{customerId}/items/{sku}` - Remove item
  - `DELETE /api/v1/cart/{customerId}` - Clear cart
- **Key Characteristics:**
  - Session-scoped data
  - High read/write frequency
  - Potential for Redis cache implementation

#### **Service 3: Checkout Service** (Orchestrator)
- **Domain:** Order Processing and Coordination
- **Responsibilities:**
  - Orchestrate checkout workflow
  - Coordinate inventory reservation, payment, and order creation
  - Handle transaction saga pattern
  - Manage compensation logic for failures
- **Data Ownership:**
  - Minimal (workflow state only)
  - No core business entities
- **API Endpoints:**
  - `POST /api/v1/checkout` - Execute checkout
  - `GET /api/v1/checkout/{checkoutId}/status` - Get checkout status
- **Key Characteristics:**
  - Stateless orchestrator
  - Calls multiple downstream services
  - Implements saga pattern for distributed transactions

#### **Service 4: Order Service**
- **Domain:** Order Management and History
- **Responsibilities:**
  - Create and store completed orders
  - Track order status lifecycle
  - Provide order history and retrieval
  - Generate order confirmations
- **Data Ownership:**
  - `Order` table
  - `OrderLine` table
- **API Endpoints:**
  - `POST /api/v1/orders` - Create order (internal, from Checkout)
  - `GET /api/v1/orders/{id}` - Get order details
  - `GET /api/v1/orders/customer/{customerId}` - Get customer order history
  - `PATCH /api/v1/orders/{id}/status` - Update order status
- **Key Characteristics:**
  - Write-once, read-many
  - Append-only data model
  - Event-driven updates

---

## 2. Deployment Model

### 2.1 Container Strategy

**Base Technology:** Docker containers orchestrated by Kubernetes

#### Container Images

Each microservice will have its own Docker image:

```dockerfile
# Example: Cart Service Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["CartService/CartService.csproj", "CartService/"]
RUN dotnet restore "CartService/CartService.csproj"
COPY . .
WORKDIR "/src/CartService"
RUN dotnet build "CartService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "CartService.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CartService.dll"]
```

#### Image Repository
- **Registry:** Azure Container Registry (ACR) or Docker Hub
- **Naming Convention:** `retailorg/[service-name]:[version]`
  - Example: `retailorg/cart-service:1.0.0`
- **Tagging Strategy:** Semantic versioning + commit SHA

### 2.2 Kubernetes Deployment

#### Namespace Organization
```yaml
namespaces:
  - retail-services       # Core business services
  - retail-infrastructure # Gateway, monitoring, logging
  - retail-data          # Database operators (if using operators)
```

#### Deployment Configuration (Example: Cart Service)
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: cart-service
  namespace: retail-services
spec:
  replicas: 3
  selector:
    matchLabels:
      app: cart-service
  template:
    metadata:
      labels:
        app: cart-service
        version: v1
    spec:
      containers:
      - name: cart-service
        image: retailorg/cart-service:1.0.0
        ports:
        - containerPort: 80
        env:
        - name: ConnectionStrings__CartDb
          valueFrom:
            secretKeyRef:
              name: cart-db-secret
              key: connection-string
        - name: ASPNETCORE_ENVIRONMENT
          value: "Production"
        resources:
          requests:
            memory: "256Mi"
            cpu: "250m"
          limits:
            memory: "512Mi"
            cpu: "500m"
        livenessProbe:
          httpGet:
            path: /health/live
            port: 80
          initialDelaySeconds: 30
          periodSeconds: 10
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 80
          initialDelaySeconds: 5
          periodSeconds: 5
---
apiVersion: v1
kind: Service
metadata:
  name: cart-service
  namespace: retail-services
spec:
  selector:
    app: cart-service
  ports:
  - protocol: TCP
    port: 80
    targetPort: 80
  type: ClusterIP
```

### 2.3 API Gateway

**Technology:** Nginx Ingress Controller or Azure API Management

#### Routing Configuration
```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: retail-gateway
  namespace: retail-infrastructure
  annotations:
    nginx.ingress.kubernetes.io/rewrite-target: /$2
    nginx.ingress.kubernetes.io/rate-limit: "100"
spec:
  rules:
  - host: api.retailapp.com
    http:
      paths:
      - path: /api/v1/products(/|$)(.*)
        pathType: Prefix
        backend:
          service:
            name: product-catalog-service
            port:
              number: 80
      - path: /api/v1/cart(/|$)(.*)
        pathType: Prefix
        backend:
          service:
            name: cart-service
            port:
              number: 80
      - path: /api/v1/checkout(/|$)(.*)
        pathType: Prefix
        backend:
          service:
            name: checkout-service
            port:
              number: 80
      - path: /api/v1/orders(/|$)(.*)
        pathType: Prefix
        backend:
          service:
            name: order-service
            port:
              number: 80
```

### 2.4 Configuration Management

#### Secrets Management
- **Tool:** Kubernetes Secrets + Azure Key Vault (or HashiCorp Vault)
- **Stored Secrets:**
  - Database connection strings
  - Payment gateway API keys
  - Service-to-service authentication tokens
  - TLS certificates

#### Configuration Strategy
```yaml
# ConfigMap for non-sensitive configuration
apiVersion: v1
kind: ConfigMap
metadata:
  name: cart-service-config
  namespace: retail-services
data:
  CACHE_TTL: "300"
  MAX_CART_ITEMS: "100"
  FEATURE_REDIS_CACHE: "true"
```

### 2.5 Database Strategy

**Approach:** Database-per-Service pattern

#### Migration Phases
1. **Phase 1 (Initial):** Shared database with schema separation
   - Each service owns specific tables
   - Logical separation enforced by convention
   - Enables service extraction without immediate data migration

2. **Phase 2 (Target):** Separate database instances
   - Each service has dedicated database
   - Physical data isolation
   - Independent scaling and backup strategies

#### Database Technology
- **RDBMS:** Azure SQL Database or SQL Server containers
- **Cache:** Redis (for Cart Service session data)
- **Future Consideration:** Event Store for checkout saga state

---

## 3. Communication Patterns

### 3.1 Synchronous Communication (HTTP/REST)

**Use Cases:** Request-response patterns where immediate response is required

**Technology:** HTTP/REST with JSON payloads

**Examples:**
- UI → API Gateway → Cart Service (get cart)
- Checkout Service → Product Service (check inventory)
- Order Service → (read operations)

**Best Practices:**
- Use semantic HTTP verbs (GET, POST, PUT, DELETE)
- Implement circuit breakers (Polly library)
- Timeout and retry policies
- Request correlation IDs for tracing

**Example: Cart Service calls Product Service**
```csharp
public class CartService
{
    private readonly HttpClient _productClient;
    private readonly IAsyncPolicy<HttpResponseMessage> _retryPolicy;

    public CartService(IHttpClientFactory httpClientFactory)
    {
        _productClient = httpClientFactory.CreateClient("ProductService");
        _retryPolicy = Policy
            .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
            .WaitAndRetryAsync(3, retryAttempt => 
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }

    public async Task<Product?> GetProductAsync(string sku)
    {
        var response = await _retryPolicy.ExecuteAsync(() =>
            _productClient.GetAsync($"/api/v1/products/{sku}"));
        
        if (!response.IsSuccessStatusCode)
            return null;
        
        return await response.Content.ReadFromJsonAsync<Product>();
    }
}
```

### 3.2 Asynchronous Communication (Event-Driven)

**Use Cases:** Fire-and-forget operations, eventual consistency, decoupling services

**Technology:** Azure Service Bus or RabbitMQ

**Message Patterns:**
1. **Events** (something happened): OrderCreated, PaymentProcessed, InventoryReserved
2. **Commands** (do something): ReserveInventory, ProcessPayment

**Event Examples:**

```csharp
// Event published by Checkout Service
public record OrderCreatedEvent(
    int OrderId,
    string CustomerId,
    decimal Total,
    List<OrderLineDto> Lines,
    DateTime CreatedUtc
);

// Event subscriber in Order Service
public class OrderCreatedEventHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly IOrderRepository _orderRepo;

    public async Task HandleAsync(OrderCreatedEvent evt)
    {
        var order = new Order
        {
            Id = evt.OrderId,
            CustomerId = evt.CustomerId,
            Total = evt.Total,
            Status = "Created",
            CreatedUtc = evt.CreatedUtc
        };
        
        await _orderRepo.CreateAsync(order);
        // Potentially publish OrderPersistedEvent
    }
}
```

**Event Bus Configuration:**
- **Topic:** `retail-events`
- **Subscriptions:** Each service subscribes to relevant events
- **Message Durability:** Persistent messages
- **Retry Policy:** Dead-letter queue after 3 retries

### 3.3 Saga Pattern for Distributed Transactions

**Use Case:** Checkout workflow requires coordination across multiple services

**Pattern:** Orchestration-based Saga (Checkout Service as orchestrator)

**Checkout Saga Steps:**
1. **Reserve Inventory** (Product Service)
2. **Process Payment** (Payment Gateway)
3. **Create Order** (Order Service)
4. **Clear Cart** (Cart Service)

**Compensation Logic (on failure):**
- Payment fails → Release inventory reservation
- Order creation fails → Refund payment + Release inventory
- Cart clear fails → Log warning (non-critical)

**Implementation:**
```csharp
public class CheckoutSaga
{
    public async Task<CheckoutResult> ExecuteAsync(CheckoutRequest request)
    {
        var sagaState = new SagaState();
        
        try
        {
            // Step 1: Reserve Inventory
            var reservation = await _productService.ReserveInventoryAsync(request.CartItems);
            sagaState.ReservationId = reservation.Id;
            
            // Step 2: Process Payment
            var payment = await _paymentGateway.ChargeAsync(request.PaymentToken, request.Total);
            if (!payment.Succeeded)
                throw new PaymentFailedException(payment.Error);
            sagaState.PaymentId = payment.Id;
            
            // Step 3: Create Order
            var order = await _orderService.CreateOrderAsync(request.ToOrder());
            sagaState.OrderId = order.Id;
            
            // Step 4: Clear Cart
            await _cartService.ClearCartAsync(request.CustomerId);
            
            return CheckoutResult.Success(order.Id);
        }
        catch (Exception ex)
        {
            await CompensateAsync(sagaState, ex);
            return CheckoutResult.Failed(ex.Message);
        }
    }

    private async Task CompensateAsync(SagaState state, Exception error)
    {
        if (state.PaymentId != null)
            await _paymentGateway.RefundAsync(state.PaymentId);
        
        if (state.ReservationId != null)
            await _productService.ReleaseReservationAsync(state.ReservationId);
        
        // Log compensation actions
    }
}
```

---

## 4. Data Access Approach

### 4.1 Database-per-Service Pattern

**Principle:** Each microservice owns its data and database schema

**Benefits:**
- Independent schema evolution
- Technology flexibility (SQL, NoSQL, cache)
- Failure isolation
- Independent scaling

**Challenges:**
- Cross-service queries require API calls
- Distributed transactions require saga pattern
- Data consistency becomes eventual

### 4.2 Service Data Ownership

| Service | Tables Owned | Data Type | Technology Choice |
|---------|-------------|-----------|-------------------|
| Product Catalog | Product, InventoryItem | Structured, relational | SQL Server / Azure SQL |
| Cart | Cart, CartLine | Session-based, high churn | Redis + SQL backup |
| Order | Order, OrderLine | Append-only, historical | SQL Server / Azure SQL |
| Checkout | SagaState (optional) | Transient workflow | In-memory / Event Store |

### 4.3 Data Consistency Models

#### Strong Consistency
- **Scope:** Within a single service boundary
- **Implementation:** ACID transactions via Entity Framework
- **Example:** Adding item to cart (Cart + CartLine in same transaction)

#### Eventual Consistency
- **Scope:** Cross-service operations
- **Implementation:** Event-driven updates + saga compensation
- **Example:** Order creation triggers inventory update event

### 4.4 Shared Reference Data

**Challenge:** Product catalog data needed by multiple services (Cart, Checkout, Order)

**Solutions:**

**Option 1: Data Replication (Recommended for Phase 1)**
- Services cache product data locally
- Subscribe to ProductUpdated events to refresh cache
- Trade-off: Slight data staleness for independence

**Option 2: API Calls**
- Services call Product Service for real-time data
- Increases coupling and latency
- Use circuit breakers to prevent cascading failures

**Option 3: BFF (Backend for Frontend)**
- API Gateway aggregates data from multiple services
- Returns composed response to client
- Services remain decoupled

### 4.5 Migration from Shared Database

**Step-by-step approach:**

1. **Logical Separation**
   - Services access only their designated tables
   - Enforce via code review and conventions
   - Share connection string initially

2. **Schema Ownership**
   - Create separate schemas per service in same database
   - Example: `cart.Cart`, `cart.CartLine` vs `order.Order`, `order.OrderLine`

3. **Read Replication**
   - Services replicate needed reference data locally
   - Subscribe to change events

4. **Physical Separation**
   - Deploy separate database instances per service
   - Migrate data using ETL pipelines
   - Update connection strings

---

## 5. Cross-Cutting Concerns

### 5.1 Authentication & Authorization

**Strategy:** JWT-based authentication via API Gateway

**Flow:**
1. User authenticates at API Gateway (OAuth 2.0 / OpenID Connect)
2. Gateway issues JWT with user claims
3. Gateway validates JWT on subsequent requests
4. Gateway forwards validated user ID to downstream services

**Service-to-Service Auth:**
- Mutual TLS (mTLS) or API keys
- Managed identities in Azure

### 5.2 Observability

#### Distributed Tracing
- **Tool:** OpenTelemetry + Jaeger or Azure Application Insights
- **Correlation ID:** Passed via `X-Correlation-ID` header through all service calls

#### Logging
- **Structured Logging:** JSON format with standardized fields
- **Centralized Aggregation:** ELK Stack (Elasticsearch, Logstash, Kibana) or Azure Monitor

#### Metrics
- **Application Metrics:** Request rate, error rate, latency (RED metrics)
- **Infrastructure Metrics:** CPU, memory, disk usage
- **Business Metrics:** Checkout success rate, cart abandonment rate

### 5.3 Resilience Patterns

1. **Circuit Breaker** (Polly library)
   - Open circuit after threshold failures
   - Half-open state for recovery testing

2. **Retry with Exponential Backoff**
   - Transient failure handling
   - Maximum retry limit

3. **Timeout**
   - Prevent hanging requests
   - Service-specific timeout values

4. **Bulkhead Isolation**
   - Separate thread pools per dependency
   - Prevent resource exhaustion

### 5.4 API Versioning

**Strategy:** URI versioning (e.g., `/api/v1/`, `/api/v2/`)

**Backward Compatibility:**
- Support at least N-1 versions
- Deprecation notices in API responses
- 6-month deprecation cycle

---

## 6. Technology Stack Summary

| Component | Technology | Justification |
|-----------|-----------|---------------|
| **Service Runtime** | ASP.NET Core 8 | Existing stack, high performance, containerizable |
| **Container Platform** | Docker + Kubernetes | Industry standard, scalable, cloud-agnostic |
| **API Gateway** | Nginx Ingress / Azure APIM | Routing, rate limiting, SSL termination |
| **Messaging** | Azure Service Bus / RabbitMQ | Reliable async messaging, retry/DLQ support |
| **Database** | SQL Server / Azure SQL | Existing expertise, transaction support |
| **Cache** | Redis | High-performance session/cart data |
| **Observability** | OpenTelemetry + Jaeger | Distributed tracing, vendor-neutral |
| **Logging** | Serilog → Azure Monitor | Structured logging, cloud integration |
| **CI/CD** | GitHub Actions → Azure DevOps | Automated build, test, deploy |
| **Secret Management** | Azure Key Vault | Secure credential storage |

---

## 7. Non-Functional Requirements

### 7.1 Performance
- **Response Time:** 95th percentile < 200ms for read operations
- **Throughput:** Support 1000 concurrent users
- **Cart Operations:** < 50ms (with Redis cache)

### 7.2 Scalability
- **Horizontal Scaling:** All services stateless, scale via replicas
- **Auto-scaling:** Kubernetes HPA based on CPU/memory thresholds
- **Database:** Read replicas for heavy read services (Product Catalog)

### 7.3 Availability
- **Target SLA:** 99.9% uptime (8.76 hours downtime/year)
- **Multi-Replica Deployment:** Minimum 3 replicas per service
- **Health Checks:** Liveness and readiness probes

### 7.4 Security
- **Authentication:** OAuth 2.0 / OpenID Connect at gateway
- **Authorization:** Role-based access control (RBAC)
- **Data Encryption:** TLS in transit, encryption at rest for databases
- **Secret Management:** No hardcoded credentials

---

## 8. Migration Strategy (High-Level)

The detailed migration plan is documented in **Migration-Plan.md**. Key principles:

1. **Strangler Fig Pattern:** Incrementally extract services while monolith remains operational
2. **Vertical Slices:** Extract end-to-end capabilities (e.g., Cart Service first)
3. **Dual-Run Period:** New service runs alongside monolith for validation
4. **Feature Flags:** Toggle between monolith and service implementation
5. **Rollback Plan:** Ability to revert to monolith at any phase

---

## 9. Success Criteria

### Technical Criteria
- ✅ All services independently deployable
- ✅ Zero-downtime deployments
- ✅ Each service has own database/schema
- ✅ 95th percentile response time < 200ms
- ✅ Circuit breakers protect against cascading failures

### Business Criteria
- ✅ No user-facing functionality loss during migration
- ✅ Checkout success rate maintained or improved
- ✅ Development velocity increased (parallel team work)

### Operational Criteria
- ✅ Automated CI/CD pipelines per service
- ✅ Distributed tracing operational
- ✅ Alerting and monitoring in place
- ✅ Runbooks for incident response

---

## 10. Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| **Increased operational complexity** | High | Invest in observability, automation, training |
| **Data consistency issues** | Medium | Implement saga pattern, event sourcing for audit |
| **Network latency** | Medium | Cache aggressively, optimize API calls, use async messaging |
| **Distributed debugging difficulty** | High | Correlation IDs, centralized logging, distributed tracing |
| **Team skill gaps** | Medium | Training on Kubernetes, microservices patterns |
| **Service dependency failures** | High | Circuit breakers, fallback mechanisms, health checks |

---

## 11. Future Considerations

### Phase 2 Enhancements
- **Event Sourcing:** Implement for Order and Checkout services
- **CQRS Pattern:** Separate read/write models for Product Catalog
- **GraphQL Gateway:** Replace REST with GraphQL for frontend
- **Service Mesh:** Istio for advanced traffic management

### Technology Evolution
- **Serverless Functions:** Move lightweight APIs to Azure Functions
- **NoSQL:** Evaluate CosmosDB for globally distributed data
- **Streaming:** Apache Kafka for real-time event processing

---

## 12. References

- [HLD.md](./HLD.md) - Current high-level design
- [LLD.md](./LLD.md) - Current low-level design
- [Migration-Plan.md](./Migration-Plan.md) - Detailed migration steps
- ADR-001: Entity Framework Core with LocalDB
- ADR-002: Razor Pages with Minimal APIs
- ADR-003: Mock Payment Gateway
- ADR-004: Guest-Only Authentication
- [Microservices Patterns](https://microservices.io/patterns/) - Chris Richardson
- [Building Microservices](https://www.oreilly.com/library/view/building-microservices-2nd/9781492034018/) - Sam Newman

---

**Document Owner:** Architecture Team  
**Review Cycle:** Quarterly or before major migrations  
**Next Review:** After Phase 1 completion
