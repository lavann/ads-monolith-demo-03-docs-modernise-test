# Migration Plan - Monolith to Microservices

**Status:** Proposed  
**Date:** 2026-01-14  
**Version:** 1.0

## Executive Summary

This document outlines a safe, incremental migration plan from the current Retail Monolith to a microservices architecture using the **Strangler Fig Pattern**. The migration is divided into phases, each delivering a working system with no big-bang rewrites.

**Key Principles:**
- ✅ Incremental extraction (one service at a time)
- ✅ Behavior preservation (no functional regressions)
- ✅ Always deployable (production-ready after each phase)
- ✅ Rollback capability (revert to monolith if needed)
- ✅ Risk-first approach (extract lowest-risk service first)

---

## Phase 0: Foundation and Preparation

**Duration:** 2-3 weeks  
**Risk Level:** Low  
**Goal:** Establish infrastructure and tooling for microservices migration

### Objectives
1. Set up container infrastructure (Docker, Kubernetes cluster)
2. Implement observability foundation (logging, tracing, metrics)
3. Establish CI/CD pipelines
4. Containerize existing monolith
5. Deploy API Gateway

### Tasks

#### Task 1: Container Infrastructure Setup
- **Action:** Provision Kubernetes cluster (AKS, EKS, or GKE)
- **Deliverables:**
  - Kubernetes cluster with 3+ nodes
  - Namespaces created (`retail-services`, `retail-infrastructure`, `retail-data`)
  - Ingress controller deployed (Nginx)
  - Helm charts repository initialized
- **Acceptance:** Cluster operational, `kubectl` commands succeed

#### Task 2: Observability Foundation
- **Action:** Deploy monitoring and logging infrastructure
- **Deliverables:**
  - OpenTelemetry collector deployed
  - Jaeger for distributed tracing
  - Prometheus + Grafana for metrics
  - Centralized logging (ELK or Azure Monitor)
  - Correlation ID middleware implemented in monolith
- **Acceptance:** Monolith logs visible in centralized system, traces captured

#### Task 3: Containerize Monolith
- **Action:** Create Docker image for existing monolith
- **Deliverables:**
  ```dockerfile
  # Dockerfile for monolith
  FROM mcr.microsoft.com/dotnet/aspnet:8.0
  WORKDIR /app
  EXPOSE 80 443
  COPY ./publish .
  ENTRYPOINT ["dotnet", "RetailMonolith.dll"]
  ```
  - Kubernetes deployment manifest for monolith
  - Health check endpoints implemented (`/health/live`, `/health/ready`)
- **Acceptance:** Monolith runs in Kubernetes, accessible via Ingress

#### Task 4: API Gateway Deployment
- **Action:** Deploy Nginx Ingress or Azure API Management
- **Deliverables:**
  - Gateway routes traffic to monolith
  - TLS termination configured
  - Rate limiting enabled (100 req/sec per IP)
  - Request/response logging
- **Acceptance:** All monolith endpoints accessible via gateway at `api.retailapp.com`

#### Task 5: CI/CD Pipeline
- **Action:** Automate build, test, and deploy
- **Deliverables:**
  - GitHub Actions workflow for monolith
  - Automated Docker image build on commit
  - Push to container registry (ACR/Docker Hub)
  - Automated deployment to dev/staging environments
- **Acceptance:** Code commit triggers automated deployment to staging

### Success Metrics
- ✅ Monolith running in Kubernetes with 99%+ uptime
- ✅ Observability dashboard shows request traces
- ✅ CI/CD deploys changes in < 10 minutes
- ✅ Zero regression in existing functionality

### Rollback Strategy
- **Trigger:** Infrastructure issues, performance degradation
- **Action:** Revert to pre-container VM-based deployment
- **Time:** < 1 hour (DNS switch back to old infrastructure)

### Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Kubernetes learning curve | Medium | High | Training sessions, hire consultant, phased rollout |
| Container networking issues | High | Medium | Thorough testing in staging, network policies validation |
| Observability data overload | Low | Medium | Start with sampling (10%), increase gradually |
| CI/CD pipeline failures | Medium | Low | Manual deployment fallback, pipeline testing |

---

## Phase 1: Extract Cart Service (FIRST SLICE)

**Duration:** 3-4 weeks  
**Risk Level:** Low  
**Goal:** Extract Cart Service as first standalone microservice

### Justification for Cart Service as First Slice

#### Why Cart Service?

1. **Minimal Dependencies:**
   - Depends only on Product Service for product info (read-only)
   - No complex orchestration logic
   - Clear input/output contract

2. **Well-Defined Boundary:**
   - Cart and CartLine tables are isolated
   - Clear domain model (AddToCart, RemoveFromCart, GetCart, ClearCart)
   - No cross-domain transactions

3. **Low Business Risk:**
   - Cart operations are idempotent (safe to retry)
   - Cart data is temporary (acceptable data loss < order creation)
   - Non-critical path (users can re-add items if cart lost)

4. **High Value Demonstration:**
   - User-facing feature (visible success)
   - Complete vertical slice (API → Service → Database)
   - Sets pattern for other services

5. **Stateless Candidate:**
   - Can implement Redis-backed caching
   - Easy to scale horizontally
   - No complex state management

#### Alternatives Considered

**❌ Checkout Service First:**
- **Rejected:** Too complex, orchestrates multiple services, requires saga pattern
- **Risk:** High coupling, difficult rollback

**❌ Product Catalog First:**
- **Rejected:** Core dependency for all services, high blast radius if issues
- **Risk:** Affects all downstream services

**❌ Order Service First:**
- **Rejected:** Requires stable Checkout, less isolated
- **Risk:** Depends on checkout workflow working correctly

### Architecture During Phase 1

```
┌─────────────────────────────────────────────┐
│            API Gateway                       │
│  (Routes to Monolith OR Cart Service)       │
└─────────────────────────────────────────────┘
           │                      │
           │                      │
           ▼                      ▼
    ┌─────────────┐      ┌──────────────┐
    │  Monolith   │      │ Cart Service │
    │  (Products, │      │  (NEW)       │
    │  Checkout,  │      │              │
    │  Orders)    │      └──────────────┘
    └─────────────┘             │
           │                    ▼
           │              ┌──────────────┐
           │              │   Cart DB    │
           │              │   (Redis +   │
           │              │    SQL)      │
           │              └──────────────┘
           ▼
    ┌─────────────┐
    │ Monolith DB │
    │ (Shared)    │
    └─────────────┘
```

### Tasks

#### Task 1: Create Cart Service Project
- **Action:** Bootstrap new ASP.NET Core Web API project
- **Deliverables:**
  ```bash
  dotnet new webapi -n CartService
  ```
  - Project structure with Controllers, Services, Models folders
  - Health check endpoints (`/health/live`, `/health/ready`)
  - OpenTelemetry integration
  - Dockerfile

#### Task 2: Implement Cart Service API
- **Action:** Implement RESTful API for cart operations
- **Deliverables:**
  ```csharp
  // API Endpoints
  GET    /api/v1/cart/{customerId}
  POST   /api/v1/cart/{customerId}/items
  DELETE /api/v1/cart/{customerId}/items/{sku}
  DELETE /api/v1/cart/{customerId}
  ```
  - CartController with proper validation
  - CartService business logic (migrated from monolith)
  - Entity models (Cart, CartLine)
  - Error handling and logging

#### Task 3: Database Strategy - Shared Schema (Phase 1A)
- **Action:** Cart Service accesses shared monolith database (temporary)
- **Deliverables:**
  - Cart Service connection string points to monolith database
  - Access limited to `Cart` and `CartLine` tables only (enforce via code review)
  - Read-only view for Products (no write access to Product/Inventory tables)
- **Rationale:** Minimize risk by avoiding immediate data migration

#### Task 4: Product Service Client
- **Action:** Cart Service needs product info for validation
- **Deliverables:**
  ```csharp
  public interface IProductClient
  {
      Task<Product?> GetProductAsync(string sku);
      Task<bool> IsProductActiveAsync(string sku);
  }
  ```
  - HTTP client implementation calling monolith's `/api/products/{sku}` endpoint
  - Fallback/circuit breaker using Polly library
  - Caching layer (cache product data for 5 minutes)
- **Acceptance:** Cart Service can add items with product validation

#### Task 5: Dual-Run Implementation (Monolith + Service)
- **Action:** Run both implementations simultaneously for validation
- **Deliverables:**
  - Feature flag: `USE_CART_SERVICE` (default: false)
  - Routing logic in API Gateway:
    ```yaml
    # Phase 1A: 10% traffic to new Cart Service
    # Phase 1B: 50% traffic to new Cart Service
    # Phase 1C: 100% traffic to new Cart Service
    ```
  - Comparison logging: Compare monolith and service responses
- **Acceptance:** Both systems produce identical results (validated via logs)

#### Task 6: Shadow Testing
- **Action:** Send copy of production traffic to Cart Service (read-only)
- **Deliverables:**
  - Nginx mirror module configured: Duplicate GET requests to Cart Service
  - Cart Service logs responses but does NOT modify data
  - Comparison script: Validate monolith vs service responses match
- **Duration:** 1 week of shadow testing
- **Acceptance:** 99.9% response parity, < 50ms latency difference

#### Task 7: Gradual Traffic Cutover
- **Action:** Incrementally shift traffic from monolith to Cart Service
- **Deliverables:**
  - **Day 1-3:** 10% traffic to Cart Service (internal users only)
  - **Day 4-7:** 25% traffic to Cart Service (monitor error rates)
  - **Day 8-10:** 50% traffic to Cart Service
  - **Day 11-14:** 100% traffic to Cart Service
- **Acceptance:** Error rate < 0.1%, latency p95 < 100ms

#### Task 8: Monolith Cart Code Deprecation
- **Action:** Remove cart logic from monolith (after 100% cutover)
- **Deliverables:**
  - Cart-related Razor Pages redirect to Cart Service API
  - CartService code in monolith marked `[Obsolete]`
  - Monolith no longer writes to Cart/CartLine tables
- **Acceptance:** Zero cart operations in monolith logs

#### Task 9: Data Migration to Separate Database (Phase 1D)
- **Action:** Move Cart/CartLine tables to dedicated Cart database
- **Deliverables:**
  - New Azure SQL database: `CartServiceDB`
  - ETL script to copy Cart/CartLine data from monolith DB
  - Cart Service connection string updated to new database
  - Monolith retains read-only view for Cart data (temporary, for rollback)
- **Acceptance:** Cart Service reads/writes only to CartServiceDB

### Success Metrics
- ✅ Cart Service handles 100% cart traffic
- ✅ Checkout workflow completes successfully (calls Cart Service to get cart)
- ✅ Zero increase in cart-related errors
- ✅ Response time: p95 < 100ms (same or better than monolith)
- ✅ Cart Service independently deployable

### Rollback Strategy

#### Rollback Trigger Conditions
- Cart Service error rate > 1%
- Cart Service latency p95 > 200ms (2x baseline)
- Data inconsistency detected (cart items lost)
- Critical bug discovered in production

#### Rollback Procedure (< 5 minutes)

**Step 1: Traffic Cutover**
```bash
# Update API Gateway to route 100% to monolith
kubectl patch ingress retail-gateway -n retail-infrastructure \
  --type='json' -p='[{"op": "replace", "path": "/spec/rules/0/http/paths/0/backend/service/name", "value": "monolith"}]'
```

**Step 2: Verify Monolith Serving Traffic**
```bash
# Check monolith logs for cart requests
kubectl logs -n retail-services deployment/monolith | grep "cart"
```

**Step 3: Data Sync (if using separate DB)**
```bash
# Run reverse ETL to sync any cart data created in Cart Service DB back to monolith DB
dotnet run --project CartMigrationTool -- --mode reverse --since "2026-01-14T10:00:00Z"
```

**Step 4: Disable Cart Service**
```bash
# Scale Cart Service to 0 replicas
kubectl scale deployment cart-service -n retail-services --replicas=0
```

**Step 5: Communicate and Monitor**
- Notify team via Slack/Teams
- Monitor monolith error rates (should return to baseline)
- Document rollback reason for postmortem

#### Rollback Testing
- **Pre-deployment:** Rehearse rollback procedure in staging
- **Frequency:** Test rollback every week during dual-run phase

### Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| **Cart data loss during cutover** | High | Low | Dual-write to both systems during transition, validate data sync |
| **Product Service dependency failure** | High | Medium | Circuit breaker, cached product data, graceful degradation |
| **Performance regression** | Medium | Medium | Load testing before cutover, auto-scaling configured |
| **Cart Service unavailability** | High | Low | 3+ replicas, health checks, auto-restart on failure |
| **Database connection pool exhaustion** | Medium | Low | Connection pooling configured, monitoring alerts |
| **Feature flag misconfiguration** | High | Low | Feature flag testing in staging, gradual rollout |

### Testing Strategy

#### Unit Tests
- CartService business logic (90%+ coverage)
- Product client with mocked HTTP responses
- Cart validation logic

#### Integration Tests
- End-to-end cart workflows: Add → Get → Remove → Clear
- Database operations with test database
- Product Service client integration

#### Load Tests
- 1000 concurrent users adding items to cart
- Target: p95 < 100ms, 0% error rate
- Tool: k6 or JMeter

#### Chaos Tests
- Kill Cart Service pod mid-request (validate auto-recovery)
- Simulate Product Service timeout (validate circuit breaker)
- Overload Cart Service (validate rate limiting)

---

## Phase 2: Extract Product Catalog Service

**Duration:** 3-4 weeks  
**Risk Level:** Medium  
**Goal:** Extract Product and Inventory management as standalone service

### Rationale
- Product Catalog is core dependency for Cart and Checkout
- Enables independent scaling for read-heavy catalog operations
- Allows caching strategies specific to product data

### Architecture During Phase 2

```
┌─────────────────────────────────────────────┐
│            API Gateway                       │
└─────────────────────────────────────────────┘
      │              │              │
      ▼              ▼              ▼
┌──────────┐  ┌──────────┐  ┌──────────┐
│ Product  │  │   Cart   │  │ Monolith │
│ Catalog  │  │ Service  │  │(Checkout,│
│ Service  │  │          │  │ Orders)  │
│ (NEW)    │  │          │  │          │
└──────────┘  └──────────┘  └──────────┘
      │              │              │
      ▼              ▼              ▼
┌──────────┐  ┌──────────┐  ┌──────────┐
│ Product  │  │  Cart DB │  │Monolith  │
│   DB     │  └──────────┘  │   DB     │
└──────────┘                └──────────┘
```

### Key Changes
- Cart Service updates Product client to call Product Catalog Service (instead of monolith)
- Monolith's Product pages proxy to Product Catalog API
- Product/InventoryItem tables migrated to Product Catalog DB

### Tasks
1. Create Product Catalog Service project
2. Implement Product and Inventory APIs
3. Implement inventory reservation mechanism (for Checkout)
4. Update Cart Service to call new Product Catalog
5. Dual-run testing (monolith + service)
6. Gradual traffic cutover (10% → 50% → 100%)
7. Data migration to separate database
8. Deprecate monolith product code

### Success Metrics
- ✅ Product Catalog handles 100% product/inventory queries
- ✅ Cart Service successfully validates products via new service
- ✅ Checkout can reserve inventory via Product Catalog API
- ✅ Response time: p95 < 50ms (read operations)

### Rollback Strategy
- Revert API Gateway routing to monolith
- Cart Service falls back to monolith product endpoints
- Retain monolith product code until Phase 3 complete

---

## Phase 3: Extract Checkout Service (Orchestrator)

**Duration:** 4-5 weeks  
**Risk Level:** High  
**Goal:** Extract checkout orchestration logic with saga pattern

### Rationale
- Checkout is complex orchestrator (cart, inventory, payment, order)
- Requires distributed transaction handling (saga pattern)
- High business value (revenue-critical path)

### Architecture During Phase 3

```
┌─────────────────────────────────────────────┐
│            API Gateway                       │
└─────────────────────────────────────────────┘
      │        │         │            │
      ▼        ▼         ▼            ▼
┌──────────┐ ┌──────┐ ┌────────┐ ┌──────────┐
│ Product  │ │ Cart │ │Checkout│ │ Monolith │
│ Catalog  │ │Service│ │Service │ │ (Orders) │
│          │ │      │ │ (NEW)  │ │          │
└──────────┘ └──────┘ └────────┘ └──────────┘
                  │         │
                  ▼         ▼
             ┌────────┐ ┌─────────┐
             │Payment │ │ Event   │
             │Gateway │ │ Bus     │
             └────────┘ └─────────┘
```

### Key Complexity: Saga Implementation
- **Step 1:** Load cart (Cart Service)
- **Step 2:** Reserve inventory (Product Catalog Service)
- **Step 3:** Process payment (Payment Gateway)
- **Step 4:** Create order (Order Service or Monolith initially)
- **Step 5:** Clear cart (Cart Service)
- **Compensation:** Rollback on any failure

### Tasks
1. Create Checkout Service with saga orchestrator
2. Implement compensation logic for each saga step
3. Integrate with Cart, Product Catalog, Payment Gateway
4. Add saga state persistence (Event Store or SQL)
5. Implement idempotency keys to prevent duplicate charges
6. Dual-run testing (critical: validate checkout parity)
7. Gradual cutover with rollback rehearsals
8. Monitor checkout success rate obsessively

### Success Metrics
- ✅ Checkout success rate ≥ 99% (same as monolith baseline)
- ✅ Saga compensation works correctly (no inventory leaks on payment failure)
- ✅ Idempotency prevents duplicate orders
- ✅ Response time: p95 < 500ms (acceptable for checkout)

### Rollback Strategy
- **High Risk:** Checkout is revenue-critical
- Feature flag to instant rollback to monolith checkout
- Shadow mode: Run new Checkout Service without committing orders (validation only)
- Full rollback drill before production cutover

---

## Phase 4: Extract Order Service

**Duration:** 2-3 weeks  
**Risk Level:** Low  
**Goal:** Extract order history and management

### Rationale
- Orders are append-only (low risk)
- Well-defined boundary (Order, OrderLine tables)
- Checkout Service becomes primary order creator

### Architecture (Final State)

```
┌─────────────────────────────────────────────┐
│            API Gateway                       │
└─────────────────────────────────────────────┘
      │         │          │           │
      ▼         ▼          ▼           ▼
┌──────────┐ ┌──────┐ ┌────────┐ ┌──────────┐
│ Product  │ │ Cart │ │Checkout│ │  Order   │
│ Catalog  │ │Service│ │Service │ │ Service  │
│          │ │      │ │        │ │  (NEW)   │
└──────────┘ └──────┘ └────────┘ └──────────┘
                           │           │
                           └─────┬─────┘
                                 ▼
                           ┌──────────┐
                           │ Event Bus│
                           │(OrderCreated)
                           └──────────┘
```

### Tasks
1. Create Order Service project
2. Implement Order API (Create, Get, List, UpdateStatus)
3. Subscribe to OrderCreated events from Checkout Service
4. Persist orders to dedicated Order DB
5. Migrate historical order data from monolith
6. Update Checkout Service to call Order Service
7. Deprecate monolith order code

### Success Metrics
- ✅ Order Service handles 100% order queries
- ✅ Order history complete and accurate
- ✅ Checkout → Order Service integration successful

### Rollback Strategy
- Revert Checkout Service to create orders directly in monolith DB
- Order Service becomes read-only fallback

---

## Phase 5: Decommission Monolith

**Duration:** 2-3 weeks  
**Risk Level:** Low  
**Goal:** Retire monolith completely

### Tasks
1. Verify all functionality migrated to microservices
2. Redirect all monolith UI pages to new service endpoints
3. Archive monolith database (backup and retain for 6 months)
4. Scale monolith deployment to 0 replicas
5. Remove monolith from API Gateway routing
6. Celebrate! 🎉

### Success Metrics
- ✅ Zero traffic to monolith for 2 weeks
- ✅ All user journeys work via microservices
- ✅ Team confident in new architecture

---

## Cross-Phase Concerns

### Authentication Migration
- **Current State:** Hardcoded "guest" customer ID
- **Target State:** JWT-based authentication at API Gateway
- **Migration:**
  - Phase 0: Implement JWT authentication in API Gateway
  - Phase 1-4: Services accept customer ID from gateway (no authentication logic in services)
  - Post-migration: Replace "guest" with real user authentication

### Data Consistency Strategy
- **Shared Database Period (Phases 1-2):** Logical separation, services own tables
- **Separate Databases (Phase 3+):** Event-driven synchronization, saga pattern

### Monitoring & Alerting Evolution
- **Phase 0:** Centralized logging and tracing infrastructure
- **Phase 1+:** Service-specific dashboards and alerts
- **Key Metrics:**
  - Request rate (per service)
  - Error rate (target: < 0.1%)
  - Latency (p50, p95, p99)
  - Saturation (CPU, memory, DB connections)

### Team Organization
- **Phase 0-1:** Single team, pair programming on new service
- **Phase 2-3:** Split into service teams (2-3 engineers per service)
- **Phase 4:** Full ownership model (team owns service end-to-end)

---

## Timeline Summary

| Phase | Duration | Risk | Deliverable |
|-------|----------|------|-------------|
| **Phase 0: Foundation** | 2-3 weeks | Low | Infrastructure, CI/CD, observability |
| **Phase 1: Cart Service** | 3-4 weeks | Low | Standalone Cart Service (FIRST SLICE) |
| **Phase 2: Product Catalog** | 3-4 weeks | Medium | Standalone Product/Inventory Service |
| **Phase 3: Checkout Service** | 4-5 weeks | High | Saga-based Checkout Orchestrator |
| **Phase 4: Order Service** | 2-3 weeks | Low | Standalone Order History Service |
| **Phase 5: Decommission** | 2-3 weeks | Low | Monolith retired |

**Total Duration:** ~16-22 weeks (~4-5 months)

---

## Decision Gates

### Gate 1: After Phase 0 (Foundation)
- **Question:** Is infrastructure stable and observable?
- **Criteria:** Monolith running in Kubernetes with 99%+ uptime, tracing operational
- **Outcome:** Proceed to Phase 1 OR address infrastructure issues

### Gate 2: After Phase 1 (Cart Service)
- **Question:** Did Cart extraction succeed? Is pattern validated?
- **Criteria:** Cart Service handles 100% traffic, zero functional regressions
- **Outcome:** Proceed to Phase 2 OR adjust strategy based on lessons learned

### Gate 3: After Phase 2 (Product Catalog)
- **Question:** Are services communicating correctly? Data migration successful?
- **Criteria:** Product Catalog operational, Cart Service integrated successfully
- **Outcome:** Proceed to Phase 3 OR refine inter-service communication

### Gate 4: After Phase 3 (Checkout Service)
- **Question:** Is saga pattern working? Revenue impact acceptable?
- **Criteria:** Checkout success rate ≥ 99%, saga compensations working
- **Outcome:** Proceed to Phase 4 OR stabilize Checkout Service

### Gate 5: After Phase 4 (Order Service)
- **Question:** Is migration complete? Ready to retire monolith?
- **Criteria:** All services operational, team confident, zero monolith traffic
- **Outcome:** Proceed to Phase 5 OR address remaining dependencies

---

## Lessons Learned (To Be Updated Post-Migration)

### Phase 1 Learnings (Cart Service)
- _To be documented after completion_

### Phase 2 Learnings (Product Catalog)
- _To be documented after completion_

### Phase 3 Learnings (Checkout Service)
- _To be documented after completion_

### Phase 4 Learnings (Order Service)
- _To be documented after completion_

---

## Appendix A: Rollback Playbook Template

**Service Name:** _______________  
**Phase:** _______________  
**Date:** _______________

### Pre-Rollback Checklist
- [ ] Confirm rollback trigger condition met (error rate > threshold, latency > threshold)
- [ ] Notify team via incident channel
- [ ] Take snapshot of current state (logs, metrics, database)
- [ ] Identify root cause (if known)

### Rollback Steps
1. **Update API Gateway Routing** (< 1 minute)
   ```bash
   kubectl patch ingress retail-gateway ...
   ```

2. **Verify Monolith Serving Traffic** (< 1 minute)
   ```bash
   kubectl logs ...
   ```

3. **Sync Data (if applicable)** (< 3 minutes)
   ```bash
   dotnet run --project MigrationTool ...
   ```

4. **Scale Down Service** (< 1 minute)
   ```bash
   kubectl scale deployment [service-name] --replicas=0
   ```

5. **Monitor Monolith** (5 minutes)
   - Check error rates return to baseline
   - Verify user journeys working

### Post-Rollback Actions
- [ ] Incident postmortem scheduled within 24 hours
- [ ] Root cause identified and documented
- [ ] Fix applied and tested in staging
- [ ] Rollback rehearsal updated with lessons learned

---

## Appendix B: Testing Checklists

### Cart Service (Phase 1) Testing
- [ ] Unit tests: 90%+ coverage
- [ ] Integration tests: All API endpoints
- [ ] Load test: 1000 concurrent users, p95 < 100ms
- [ ] Chaos test: Kill pod mid-request, verify auto-recovery
- [ ] Shadow test: 1 week, 99.9% parity with monolith
- [ ] Rollback drill: Execute rollback in staging successfully

### Product Catalog (Phase 2) Testing
- [ ] Unit tests: 90%+ coverage
- [ ] Integration tests: Product CRUD, inventory reservation
- [ ] Load test: 5000 concurrent users (read-heavy), p95 < 50ms
- [ ] Cache test: Verify cache hit rates > 80%
- [ ] Rollback drill: Execute rollback in staging successfully

### Checkout Service (Phase 3) Testing
- [ ] Unit tests: Saga orchestration logic
- [ ] Integration tests: Full checkout flow end-to-end
- [ ] Saga compensation tests: Payment failure → inventory released
- [ ] Idempotency test: Duplicate request → single order
- [ ] Load test: 500 concurrent checkouts, p95 < 500ms
- [ ] Rollback drill: CRITICAL - rehearse 3+ times before production

### Order Service (Phase 4) Testing
- [ ] Unit tests: Order CRUD operations
- [ ] Integration tests: OrderCreated event handling
- [ ] Data migration test: Historical orders migrated correctly
- [ ] Load test: 1000 concurrent order queries, p95 < 100ms

---

## Appendix C: Success Metrics Dashboard

**Live Dashboard URL:** _[To be added in Phase 0]_

### Key Metrics (Per Service)

| Metric | Target | Alert Threshold |
|--------|--------|-----------------|
| **Availability** | 99.9% | < 99.5% |
| **Error Rate** | < 0.1% | > 1% |
| **Latency (p95)** | < 200ms | > 500ms |
| **Request Rate** | Baseline +/- 20% | Baseline +/- 50% |
| **Saga Success Rate** (Checkout) | > 99% | < 98% |
| **Data Sync Lag** | < 1 second | > 5 seconds |

---

## References

- [Target-Architecture.md](./Target-Architecture.md) - Detailed target architecture
- [HLD.md](./HLD.md) - Current system high-level design
- [LLD.md](./LLD.md) - Current system low-level design
- [Strangler Fig Pattern](https://martinfowler.com/bliki/StranglerFigApplication.html) - Martin Fowler
- [Saga Pattern](https://microservices.io/patterns/data/saga.html) - Chris Richardson

---

**Document Owner:** Architecture Team  
**Review Cycle:** After each phase completion  
**Next Review:** After Phase 1 (Cart Service) completion
