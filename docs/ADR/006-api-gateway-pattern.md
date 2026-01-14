# ADR-006: API Gateway Pattern

**Status:** Accepted

**Date:** 2026-01-14

**Context:** In a microservices architecture, clients need to interact with multiple services. Direct client-to-service communication creates challenges: clients must know multiple endpoint URLs, handle authentication per service, deal with changing service locations, and aggregate data from multiple services. An API Gateway provides a single entry point that addresses these concerns.

---

## Decision

Implement an **API Gateway** as the single entry point for all client requests to the microservices backend.

**Technology Choice:** Nginx Ingress Controller (Kubernetes-native) with option to migrate to Azure API Management for advanced features.

---

## Rationale

### Why API Gateway?

1. **Single Entry Point**
   - Clients interact with one endpoint (`api.retailapp.com`)
   - Simplified client configuration (mobile apps, web frontend)
   - Easier to manage external vs internal APIs

2. **Routing & Composition**
   - Route requests to appropriate microservices based on URL path
   - Support API versioning (`/api/v1/`, `/api/v2/`)
   - Aggregate responses from multiple services (Backend for Frontend pattern)

3. **Security & Authentication**
   - Centralized authentication (OAuth 2.0, JWT validation)
   - Single TLS termination point
   - Rate limiting and DDoS protection
   - API key management

4. **Cross-Cutting Concerns**
   - Request/response logging and correlation IDs
   - Caching for frequently accessed data
   - Request/response transformation (protocol translation)
   - Retry and circuit breaker policies

5. **Operational Benefits**
   - Monitor and meter API usage
   - A/B testing and canary deployments (traffic splitting)
   - Throttling and quota management
   - Centralized policy enforcement

### Why Nginx Ingress Controller?

**For Initial Implementation (Phase 0-2):**

1. **Kubernetes Native**
   - Built-in Kubernetes Ingress resource support
   - Automatic service discovery via Kubernetes Services
   - Scales with cluster (horizontal scaling)

2. **Open Source & Mature**
   - Battle-tested in production worldwide
   - Large community and extensive documentation
   - No licensing costs

3. **Performance**
   - High throughput, low latency
   - Efficient reverse proxy (C-based)
   - Minimal resource overhead

4. **Simplicity**
   - Configuration via Kubernetes Ingress YAML
   - No additional infrastructure to manage
   - Deployed as Kubernetes Deployment + Service

5. **Feature Set (Sufficient for Phase 1-2)**
   - SSL/TLS termination
   - Path-based and host-based routing
   - Rate limiting (via annotations)
   - Request rewriting and redirects
   - CORS handling
   - Basic authentication

### Why Consider Azure API Management (Future)?

**For Advanced Use Cases (Phase 3+):**

1. **Developer Portal**
   - Self-service API documentation (OpenAPI/Swagger)
   - API key provisioning
   - Interactive API testing

2. **Advanced Policies**
   - Request/response transformation (XML/JSON conversion)
   - Caching policies (Redis-backed)
   - IP filtering and geo-blocking
   - Mock responses for API testing

3. **Analytics & Monitoring**
   - Detailed API usage analytics
   - Per-API and per-consumer metrics
   - Built-in dashboards

4. **Monetization**
   - Usage-based billing
   - Tiered subscription plans
   - Quota management per API consumer

5. **Hybrid Connectivity**
   - Connect to on-premises APIs
   - VNet integration

**Decision:** Start with Nginx, migrate to APIM if advanced features needed.

---

## Implementation Details

### Architecture

```
┌──────────────────────────────────────────────┐
│           External Clients                   │
│  (Web Browser, Mobile App, External API)    │
└──────────────────────────────────────────────┘
                    │
                    │ HTTPS
                    ▼
┌──────────────────────────────────────────────┐
│            API Gateway                       │
│  (Nginx Ingress Controller)                  │
│                                              │
│  - TLS Termination                           │
│  - Authentication (JWT validation)           │
│  - Rate Limiting                             │
│  - Routing (path-based)                      │
│  - Request Logging (correlation IDs)         │
└──────────────────────────────────────────────┘
      │           │           │           │
      │ HTTP      │ HTTP      │ HTTP      │ HTTP
      ▼           ▼           ▼           ▼
┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐
│ Product  │ │   Cart   │ │ Checkout │ │  Order   │
│ Catalog  │ │ Service  │ │ Service  │ │ Service  │
│ Service  │ │          │ │          │ │          │
└──────────┘ └──────────┘ └──────────┘ └──────────┘
```

### Nginx Ingress Configuration

**Installation:**
```bash
# Install Nginx Ingress Controller via Helm
helm repo add ingress-nginx https://kubernetes.github.io/ingress-nginx
helm repo update
helm install ingress-nginx ingress-nginx/ingress-nginx \
  --namespace retail-infrastructure \
  --create-namespace \
  --set controller.replicaCount=3 \
  --set controller.metrics.enabled=true \
  --set controller.podAnnotations."prometheus\.io/scrape"=true
```

**Ingress Resource (Routing):**
```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: retail-gateway
  namespace: retail-infrastructure
  annotations:
    # Rewrite URL (remove /api/v1/cart prefix before forwarding)
    nginx.ingress.kubernetes.io/rewrite-target: /$2
    
    # Rate limiting (100 requests per second per IP)
    nginx.ingress.kubernetes.io/rate-limit: "100"
    nginx.ingress.kubernetes.io/rate-limit-burst: "20"
    
    # CORS configuration
    nginx.ingress.kubernetes.io/enable-cors: "true"
    nginx.ingress.kubernetes.io/cors-allow-origin: "https://www.retailapp.com"
    nginx.ingress.kubernetes.io/cors-allow-methods: "GET, POST, PUT, DELETE, OPTIONS"
    
    # SSL redirect
    nginx.ingress.kubernetes.io/ssl-redirect: "true"
    
    # TLS certificate (Let's Encrypt via cert-manager)
    cert-manager.io/cluster-issuer: "letsencrypt-prod"
    
    # Request size limit (10MB)
    nginx.ingress.kubernetes.io/proxy-body-size: "10m"
    
    # Timeout settings
    nginx.ingress.kubernetes.io/proxy-connect-timeout: "10"
    nginx.ingress.kubernetes.io/proxy-send-timeout: "30"
    nginx.ingress.kubernetes.io/proxy-read-timeout: "30"
spec:
  ingressClassName: nginx
  tls:
  - hosts:
    - api.retailapp.com
    secretName: retail-api-tls-secret
  rules:
  - host: api.retailapp.com
    http:
      paths:
      # Product Catalog Service
      - path: /api/v1/products(/|$)(.*)
        pathType: Prefix
        backend:
          service:
            name: product-catalog-service
            port:
              number: 80
      
      # Cart Service
      - path: /api/v1/cart(/|$)(.*)
        pathType: Prefix
        backend:
          service:
            name: cart-service
            port:
              number: 80
      
      # Checkout Service
      - path: /api/v1/checkout(/|$)(.*)
        pathType: Prefix
        backend:
          service:
            name: checkout-service
            port:
              number: 80
      
      # Order Service
      - path: /api/v1/orders(/|$)(.*)
        pathType: Prefix
        backend:
          service:
            name: order-service
            port:
              number: 80
      
      # Health check (aggregate)
      - path: /health
        pathType: Exact
        backend:
          service:
            name: health-aggregator-service
            port:
              number: 80
```

### Authentication & Authorization

**JWT Validation (via Nginx + Auth Service):**

Option 1: **Nginx Auth Request** (Recommended for Phase 1)
```yaml
annotations:
  nginx.ingress.kubernetes.io/auth-url: "http://auth-service.retail-infrastructure.svc.cluster.local/auth/validate"
  nginx.ingress.kubernetes.io/auth-response-headers: "X-User-Id, X-User-Role"
```

Flow:
1. Client sends request with `Authorization: Bearer <JWT>` header
2. Nginx forwards token to `auth-service` for validation
3. Auth service validates JWT (signature, expiration, claims)
4. Auth service returns 200 (valid) or 401 (invalid)
5. If valid, Nginx adds `X-User-Id` header and forwards to backend service
6. Backend service trusts `X-User-Id` header (internal network)

**Auth Service Pseudocode:**
```csharp
[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly JwtSecurityTokenHandler _tokenHandler;
    private readonly TokenValidationParameters _validationParams;

    [HttpGet("validate")]
    public IActionResult ValidateToken()
    {
        var authHeader = Request.Headers["Authorization"].FirstOrDefault();
        if (authHeader == null || !authHeader.StartsWith("Bearer "))
            return Unauthorized();

        var token = authHeader.Substring("Bearer ".Length);
        
        try
        {
            var principal = _tokenHandler.ValidateToken(token, _validationParams, out _);
            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var role = principal.FindFirst(ClaimTypes.Role)?.Value;

            Response.Headers.Add("X-User-Id", userId);
            Response.Headers.Add("X-User-Role", role);
            
            return Ok();
        }
        catch (SecurityTokenException)
        {
            return Unauthorized();
        }
    }
}
```

Option 2: **API Management (Future Migration)**
- Built-in JWT validation policy
- No separate auth service needed
- Azure AD integration for token issuance

### Rate Limiting

**Per-IP Rate Limiting:**
```yaml
annotations:
  nginx.ingress.kubernetes.io/rate-limit: "100"  # 100 req/sec per IP
  nginx.ingress.kubernetes.io/rate-limit-burst: "20"  # Allow burst of 20 over limit
```

**Per-User Rate Limiting (Future):**
- Requires custom Nginx Lua script or APIM
- Rate limit based on `X-User-Id` header
- Example: 1000 req/hour per authenticated user

### Request Logging & Tracing

**Correlation ID Injection:**
```yaml
annotations:
  nginx.ingress.kubernetes.io/configuration-snippet: |
    # Generate correlation ID if not present
    set $correlation_id $http_x_correlation_id;
    if ($correlation_id = "") {
      set $correlation_id $request_id;
    }
    proxy_set_header X-Correlation-ID $correlation_id;
    
    # Add to response headers for debugging
    add_header X-Correlation-ID $correlation_id;
```

**Access Logging:**
- Nginx logs all requests to stdout (captured by log aggregator)
- Log format includes: timestamp, IP, method, path, status, latency, correlation ID
- Example: `2026-01-14T10:30:45Z 192.168.1.10 GET /api/v1/cart/guest 200 45ms corr-id-12345`

### Caching (Future Enhancement)

**Response Caching:**
```yaml
annotations:
  nginx.ingress.kubernetes.io/server-snippet: |
    proxy_cache_path /tmp/nginx-cache levels=1:2 keys_zone=api_cache:10m max_size=1g inactive=60m;
    proxy_cache api_cache;
    proxy_cache_valid 200 5m;  # Cache 200 responses for 5 minutes
    proxy_cache_key "$scheme$request_method$host$request_uri";
    add_header X-Cache-Status $upstream_cache_status;
```

**Use Cases:**
- Product catalog (GET /api/v1/products) - cache for 5 minutes
- Public content (privacy policy, terms of service)
- **NOT for:** Cart, checkout, orders (user-specific data)

### SSL/TLS Configuration

**Certificate Management (cert-manager):**
```bash
# Install cert-manager
kubectl apply -f https://github.com/cert-manager/cert-manager/releases/download/v1.13.0/cert-manager.yaml

# Create ClusterIssuer for Let's Encrypt
kubectl apply -f - <<EOF
apiVersion: cert-manager.io/v1
kind: ClusterIssuer
metadata:
  name: letsencrypt-prod
spec:
  acme:
    server: https://acme-v02.api.letsencrypt.org/directory
    email: admin@retailapp.com
    privateKeySecretRef:
      name: letsencrypt-prod-key
    solvers:
    - http01:
        ingress:
          class: nginx
EOF
```

Certificate automatically issued and renewed by cert-manager.

### Canary Deployments (Traffic Splitting)

**Canary Ingress (10% traffic to v2):**
```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: retail-gateway-canary
  namespace: retail-infrastructure
  annotations:
    nginx.ingress.kubernetes.io/canary: "true"
    nginx.ingress.kubernetes.io/canary-weight: "10"  # 10% traffic
spec:
  ingressClassName: nginx
  rules:
  - host: api.retailapp.com
    http:
      paths:
      - path: /api/v1/cart(/|$)(.*)
        pathType: Prefix
        backend:
          service:
            name: cart-service-v2  # New version
            port:
              number: 80
```

**Use Case:**
- Deploy Cart Service v2 alongside v1
- Route 10% traffic to v2 for validation
- Monitor error rates and latency
- Gradually increase to 50%, then 100%
- Rollback by deleting canary Ingress

---

## Consequences

### Positive

1. **Simplified Client Integration**
   - Single endpoint for all APIs
   - Clients don't need service discovery
   - Consistent authentication model

2. **Security**
   - Centralized authentication and authorization
   - TLS termination (services use HTTP internally)
   - Rate limiting prevents abuse

3. **Flexibility**
   - Easy to add new services (update Ingress rules)
   - Support API versioning without client changes
   - Traffic splitting for A/B testing

4. **Observability**
   - Centralized request logging
   - Single point for monitoring API usage
   - Correlation IDs for distributed tracing

5. **Operational**
   - Zero client downtime when services change locations
   - Canary deployments for safer rollouts
   - Easy to implement caching

### Negative

1. **Single Point of Failure**
   - Gateway outage affects all services
   - **Mitigation:** Deploy multiple Ingress replicas (3+), multi-AZ

2. **Performance Overhead**
   - Additional network hop (client → gateway → service)
   - Latency increase: ~5-10ms per request
   - **Mitigation:** Gateway co-located with services (same datacenter/region)

3. **Complexity**
   - Additional component to manage and monitor
   - Configuration complexity (Ingress annotations)
   - Potential bottleneck if not properly scaled

4. **Feature Limitations (Nginx)**
   - Limited request/response transformation capabilities
   - No built-in API analytics dashboard
   - Basic policy enforcement only
   - **Mitigation:** Migrate to APIM for advanced features

5. **Cost**
   - Nginx is free, but consumes cluster resources
   - APIM has monthly costs ($0.035/hour for Developer tier = ~$25/month)

### Operational

1. **Monitoring**
   - Nginx Ingress exports Prometheus metrics
   - Key metrics: request rate, error rate, latency, upstream status
   - Alert on: error rate > 1%, p95 latency > 500ms

2. **Scaling**
   - Horizontal scaling: Add more Ingress replicas (HPA based on CPU/memory)
   - Vertical scaling: Increase CPU/memory per replica

3. **High Availability**
   - Minimum 3 replicas across multiple availability zones
   - Readiness/liveness probes configured
   - Pod anti-affinity (spread replicas across nodes)

4. **Certificate Renewal**
   - Automated by cert-manager (Let's Encrypt 90-day expiry, auto-renews at 60 days)
   - Alert if certificate expiry < 30 days (manual intervention needed)

---

## Alternatives Considered

### Alternative 1: Direct Service Access (No Gateway)
- **Pros:**
  - No additional component
  - Lower latency (one less hop)
  - Simpler architecture
- **Cons:**
  - Clients must know all service URLs
  - No centralized authentication or rate limiting
  - Client changes required when services move
  - Difficult to implement cross-cutting concerns
- **Rejected:** Unmanageable at scale, poor security

### Alternative 2: Azure API Management (from start)
- **Pros:**
  - Rich feature set (developer portal, analytics, transformation)
  - Managed service (less operational overhead)
  - Built-in JWT validation and caching
- **Cons:**
  - Monthly cost ($25-$3000+ depending on tier)
  - Potential vendor lock-in
  - Overkill for initial phases (simple routing sufficient)
  - External dependency (not Kubernetes-native)
- **Decision:** Defer to Phase 3+, use Nginx initially

### Alternative 3: Kong Gateway
- **Pros:**
  - Open source and feature-rich
  - Plugin ecosystem (authentication, rate limiting, transformation)
  - Better than Nginx for complex policies
- **Cons:**
  - Requires PostgreSQL database (additional dependency)
  - More complex to configure than Nginx Ingress
  - Overkill for Phase 1 requirements
  - Smaller community than Nginx in Kubernetes ecosystem
- **Rejected:** Nginx sufficient for initial needs, Kong adds complexity

### Alternative 4: Istio Service Mesh
- **Pros:**
  - Advanced traffic management (circuit breaking, retries, timeouts)
  - Mutual TLS between services
  - Distributed tracing built-in
  - API gateway capabilities via Istio Gateway
- **Cons:**
  - Very complex (sidecars, control plane, steep learning curve)
  - Significant operational overhead
  - Resource intensive (sidecar per pod)
  - Overkill for current requirements
- **Rejected:** Too complex for Phase 1, consider for Phase 4+ if needed

### Alternative 5: BFF (Backend for Frontend)
- **Pros:**
  - Optimized APIs per client type (web, mobile, IoT)
  - Reduces client-side complexity (data aggregation)
  - Tailored security policies per frontend
- **Cons:**
  - Multiple BFFs to build and maintain (one per client type)
  - Additional development effort
  - Not mutually exclusive with API Gateway (can use both)
- **Decision:** Complement API Gateway with BFF pattern in future if needed

---

## Migration Strategy

### Phase 0: Deploy API Gateway
1. **Install Nginx Ingress Controller** (see Kubernetes ADR)
2. **Configure DNS:** Point `api.retailapp.com` to Ingress LoadBalancer IP
3. **Set up TLS:** Install cert-manager, create ClusterIssuer, configure TLS in Ingress
4. **Route to Monolith:** Initially, all paths route to monolith
5. **Validate:** Test all existing endpoints through gateway

### Phase 1-4: Incremental Service Routing
- As each service is extracted, update Ingress rules to route traffic
- Example: Phase 1 (Cart Service) → Add `/api/v1/cart` path to cart-service backend
- Use feature flags and canary deployments for gradual cutover

### Future: Migrate to APIM (Optional)
- If advanced features needed (developer portal, analytics, quotas)
- Deploy APIM in front of Kubernetes cluster
- APIM forwards to Kubernetes Ingress (acting as internal gateway)
- Or: APIM directly routes to Kubernetes Services (replace Ingress)

---

## Success Criteria

- ✅ All API requests routed through single endpoint (`api.retailapp.com`)
- ✅ TLS certificate valid and auto-renewing
- ✅ Rate limiting prevents abuse (tested with load test)
- ✅ Authentication validates JWT tokens correctly
- ✅ Correlation IDs present in all request logs
- ✅ Gateway p95 latency < 50ms (excluding backend service time)
- ✅ Gateway uptime ≥ 99.9%

---

## References

- [Nginx Ingress Controller Documentation](https://kubernetes.github.io/ingress-nginx/)
- [API Gateway Pattern](https://microservices.io/patterns/apigateway.html) - Chris Richardson
- [Azure API Management Overview](https://learn.microsoft.com/en-us/azure/api-management/)
- [Target-Architecture.md](./Target-Architecture.md) - Deployment model
- [Migration-Plan.md](./Migration-Plan.md) - Phase 0 gateway setup

---

## Notes

- **Security:** Enable WAF (Web Application Firewall) in production for DDoS and OWASP Top 10 protection
- **Cost Optimization:** Monitor Ingress resource usage, right-size replicas
- **Observability:** Integrate with Prometheus for metrics, Grafana for dashboards

---

**Review Cycle:** After each major service extraction phase  
**Next Review:** After Phase 2 (Product Catalog extraction)
