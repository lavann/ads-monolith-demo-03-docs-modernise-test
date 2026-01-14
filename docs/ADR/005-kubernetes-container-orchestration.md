# ADR-005: Kubernetes for Container Orchestration

**Status:** Accepted

**Date:** 2026-01-14

**Context:** The migration from monolith to microservices requires a container orchestration platform to manage deployment, scaling, networking, and lifecycle of multiple containerized services. The platform must support the target architecture outlined in Target-Architecture.md and enable incremental migration with rollback capabilities.

---

## Decision

Use **Kubernetes** as the container orchestration platform for deploying and managing microservices.

---

## Rationale

### Why Container Orchestration?

The microservices architecture requires:
- **Service Discovery:** Services need to find and communicate with each other
- **Load Balancing:** Distribute traffic across multiple service instances
- **Auto-Scaling:** Scale services based on demand (horizontal pod autoscaling)
- **Health Monitoring:** Automatic restart of failed containers
- **Rolling Deployments:** Zero-downtime deployments with automatic rollback
- **Configuration Management:** Centralized config and secrets management
- **Network Policies:** Secure service-to-service communication

Manual management of these concerns is operationally prohibitive at scale.

### Why Kubernetes?

1. **Industry Standard**
   - De facto standard for container orchestration
   - Large ecosystem and community support
   - Extensive tooling (Helm, kubectl, operators)
   - Most cloud providers offer managed Kubernetes (AKS, EKS, GKE)

2. **Cloud Agnostic**
   - Portable across AWS, Azure, GCP, on-premises
   - Reduces vendor lock-in
   - Consistent API and deployment model regardless of infrastructure

3. **Mature and Production-Ready**
   - Battle-tested at scale (used by Google, Spotify, Airbnb, etc.)
   - CNCF graduated project (highest maturity level)
   - Stable API (v1 resources well-established)

4. **Rich Feature Set**
   - **Deployments:** Declarative updates with rollback support
   - **Services:** Built-in load balancing and service discovery
   - **ConfigMaps & Secrets:** Configuration and credential management
   - **Ingress:** HTTP routing and SSL termination
   - **StatefulSets:** Stateful workload support (databases)
   - **RBAC:** Fine-grained access control
   - **Network Policies:** Microsegmentation for security
   - **Horizontal Pod Autoscaling:** Automatic scaling based on metrics

5. **Observability Integration**
   - Native support for health checks (liveness, readiness, startup probes)
   - Prometheus integration for metrics
   - OpenTelemetry support for tracing
   - Centralized logging via sidecar containers

6. **Ecosystem Compatibility**
   - Helm charts for package management
   - Operators for complex application lifecycle (databases, message queues)
   - Service meshes (Istio, Linkerd) for advanced traffic management
   - GitOps tools (ArgoCD, Flux) for declarative deployments

7. **Development Workflow**
   - Local development: Minikube, Kind, Docker Desktop
   - Consistent environment from dev to production
   - CI/CD integration well-documented

---

## Implementation Details

### Cluster Configuration

**Managed Kubernetes Service (Recommended):**
- **Azure:** Azure Kubernetes Service (AKS)
- **AWS:** Elastic Kubernetes Service (EKS)
- **GCP:** Google Kubernetes Engine (GKE)
- **Rationale:** Managed control plane, automated updates, lower operational overhead

**Cluster Sizing (Initial):**
- **Node Pool:** 3-5 nodes (VM size: 4 vCPU, 16 GB RAM per node)
- **Auto-scaling:** Enabled (min: 3 nodes, max: 10 nodes)
- **Availability Zones:** Multi-AZ deployment for high availability

### Namespace Strategy

Organize workloads by function:
```yaml
namespaces:
  - retail-services       # Microservices (cart, checkout, order, product)
  - retail-infrastructure # API gateway, monitoring, logging
  - retail-data          # Databases, Redis, message queue (if self-hosted)
  - retail-staging       # Staging environment in same cluster (optional)
```

### Deployment Pattern

**Deployment Manifest Example (Cart Service):**
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: cart-service
  namespace: retail-services
  labels:
    app: cart-service
    version: v1.0.0
spec:
  replicas: 3
  strategy:
    type: RollingUpdate
    rollingUpdate:
      maxSurge: 1
      maxUnavailable: 0  # Zero-downtime deployments
  selector:
    matchLabels:
      app: cart-service
  template:
    metadata:
      labels:
        app: cart-service
        version: v1.0.0
    spec:
      containers:
      - name: cart-service
        image: retailorg/cart-service:1.0.0
        ports:
        - containerPort: 80
          name: http
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: "Production"
        - name: ConnectionStrings__CartDb
          valueFrom:
            secretKeyRef:
              name: cart-db-secret
              key: connection-string
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
          timeoutSeconds: 5
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 80
          initialDelaySeconds: 5
          periodSeconds: 5
          timeoutSeconds: 3
```

### Service Discovery

**Service Manifest:**
```yaml
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
  type: ClusterIP  # Internal service, not exposed externally
```

Services are accessible via DNS: `http://cart-service.retail-services.svc.cluster.local`

### Configuration & Secrets

**ConfigMap (non-sensitive):**
```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: cart-service-config
  namespace: retail-services
data:
  CACHE_TTL: "300"
  MAX_CART_ITEMS: "100"
  LOG_LEVEL: "Information"
```

**Secret (sensitive):**
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: cart-db-secret
  namespace: retail-services
type: Opaque
data:
  connection-string: <base64-encoded-value>
```

**Integration with Azure Key Vault (Recommended):**
- Use Azure Key Vault Provider for Secrets Store CSI Driver
- Secrets synced from Key Vault to Kubernetes at pod startup
- No secrets stored in Git or Kubernetes etcd plaintext

### Ingress (API Gateway)

**Nginx Ingress Controller:**
```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: retail-gateway
  namespace: retail-infrastructure
  annotations:
    nginx.ingress.kubernetes.io/rewrite-target: /$2
    nginx.ingress.kubernetes.io/ssl-redirect: "true"
    nginx.ingress.kubernetes.io/rate-limit: "100"
    cert-manager.io/cluster-issuer: "letsencrypt-prod"
spec:
  ingressClassName: nginx
  tls:
  - hosts:
    - api.retailapp.com
    secretName: retail-tls-secret
  rules:
  - host: api.retailapp.com
    http:
      paths:
      - path: /api/v1/cart(/|$)(.*)
        pathType: Prefix
        backend:
          service:
            name: cart-service
            port:
              number: 80
      - path: /api/v1/products(/|$)(.*)
        pathType: Prefix
        backend:
          service:
            name: product-catalog-service
            port:
              number: 80
```

### Auto-Scaling

**Horizontal Pod Autoscaler (HPA):**
```yaml
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: cart-service-hpa
  namespace: retail-services
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: cart-service
  minReplicas: 3
  maxReplicas: 10
  metrics:
  - type: Resource
    resource:
      name: cpu
      target:
        type: Utilization
        averageUtilization: 70
  - type: Resource
    resource:
      name: memory
      target:
        type: Utilization
        averageUtilization: 80
```

### Rollback Capability

Kubernetes native rollback:
```bash
# Rollback to previous deployment
kubectl rollout undo deployment/cart-service -n retail-services

# Rollback to specific revision
kubectl rollout undo deployment/cart-service -n retail-services --to-revision=3

# Check rollout history
kubectl rollout history deployment/cart-service -n retail-services
```

---

## Consequences

### Positive

1. **Operational Efficiency**
   - Automated deployments, scaling, and self-healing
   - Reduced manual intervention
   - Faster incident recovery

2. **Developer Productivity**
   - Consistent deployment model across services
   - Local development mirrors production (Minikube)
   - CI/CD integration straightforward

3. **Scalability**
   - Horizontal scaling on demand
   - Efficient resource utilization (bin packing)
   - Support for thousands of services if needed

4. **Resilience**
   - Automatic pod restarts on failure
   - Rolling updates minimize downtime
   - Multi-AZ deployment for high availability

5. **Cloud Portability**
   - Avoid vendor lock-in
   - Migrate between cloud providers with minimal changes

6. **Ecosystem Integration**
   - Rich tooling (Helm, Prometheus, Jaeger, Grafana)
   - Strong community and commercial support

### Negative

1. **Complexity**
   - Steep learning curve for team unfamiliar with Kubernetes
   - Complex YAML configurations
   - Debugging distributed systems harder than monolith

2. **Operational Overhead**
   - Requires Kubernetes expertise for troubleshooting
   - Cluster upgrades require planning and testing
   - More moving parts (control plane, nodes, networking)

3. **Cost**
   - Managed Kubernetes services have monthly fees
   - Resource overhead (control plane, system pods consume resources)
   - Minimum cluster size for HA (3+ nodes)

4. **Initial Setup Time**
   - Cluster provisioning and configuration (Phase 0: 2-3 weeks)
   - Learning curve for team
   - Observability and monitoring setup

5. **Over-Engineering Risk**
   - Kubernetes may be overkill for very small deployments (< 5 services)
   - Simpler alternatives exist (Docker Compose, cloud PaaS)

### Operational

1. **Team Training**
   - Mandatory Kubernetes fundamentals training for all engineers
   - Hands-on labs with Minikube/Kind
   - Dedicate 2-3 weeks for upskilling

2. **Monitoring & Alerting**
   - Prometheus for metrics collection
   - Grafana for dashboards
   - Alertmanager for alert routing
   - Deploy in Phase 0 (see Migration-Plan.md)

3. **Backup & Disaster Recovery**
   - Velero for cluster-level backups
   - Database backups independent of Kubernetes
   - Document disaster recovery procedures

4. **Cost Management**
   - Enable cluster autoscaling to optimize node usage
   - Right-size resource requests/limits per service
   - Monitor and audit resource consumption monthly

---

## Alternatives Considered

### Alternative 1: Docker Compose
- **Pros:**
  - Simple, minimal learning curve
  - Good for local development
  - Single YAML file per stack
- **Cons:**
  - No auto-scaling or self-healing
  - Single-host limitation (no clustering)
  - No built-in load balancing or service discovery
  - Not production-grade for microservices
- **Rejected:** Insufficient for production microservices deployment

### Alternative 2: Cloud PaaS (Azure App Service, AWS ECS)
- **Pros:**
  - Managed platform, less operational overhead
  - Simpler deployment model
  - Built-in scaling and monitoring
- **Cons:**
  - Vendor lock-in (harder to migrate between clouds)
  - Less flexibility (constrained by platform capabilities)
  - May not support all microservices patterns (service mesh, custom networking)
  - Cost increases significantly with scale
- **Rejected:** Prioritize cloud portability and flexibility for future growth

### Alternative 3: Docker Swarm
- **Pros:**
  - Simpler than Kubernetes
  - Native Docker orchestration
  - Easier to learn
- **Cons:**
  - Smaller ecosystem and community
  - Less feature-rich than Kubernetes
  - Uncertain long-term support (Docker, Inc. focus shifted)
  - Fewer managed offerings (no major cloud provider support)
- **Rejected:** Kubernetes is industry standard with better ecosystem

### Alternative 4: Nomad (HashiCorp)
- **Pros:**
  - Simpler architecture than Kubernetes
  - Supports non-containerized workloads (VMs, binaries)
  - Good integration with HashiCorp stack (Consul, Vault)
- **Cons:**
  - Smaller community and ecosystem
  - Fewer managed offerings
  - Less tooling and operator support
- **Rejected:** Kubernetes ecosystem and community support outweigh simplicity benefits

### Alternative 5: Serverless (Azure Functions, AWS Lambda)
- **Pros:**
  - Zero infrastructure management
  - Pay-per-invocation pricing
  - Infinite auto-scaling
- **Cons:**
  - Cold start latency (unacceptable for synchronous APIs)
  - Vendor lock-in (proprietary programming models)
  - Stateful workloads difficult (databases still needed)
  - Not suitable for long-running checkout saga orchestration
- **Rejected:** Better fit for event-driven background tasks, not core API services

---

## Migration Strategy

### Phase 0: Foundation (see Migration-Plan.md)
1. **Cluster Provisioning:**
   - Provision AKS/EKS/GKE cluster
   - Configure node pools and auto-scaling
   - Set up namespaces

2. **Networking:**
   - Install Nginx Ingress Controller
   - Configure DNS (api.retailapp.com → Ingress IP)
   - Set up TLS certificates (Let's Encrypt + cert-manager)

3. **Observability:**
   - Deploy Prometheus + Grafana
   - Deploy Jaeger for tracing
   - Configure log aggregation (Fluentd → Elasticsearch → Kibana or Azure Monitor)

4. **CI/CD:**
   - GitHub Actions workflow to build Docker images
   - Push images to ACR/Docker Hub
   - Deploy to Kubernetes using `kubectl apply` or Helm

5. **Validation:**
   - Deploy containerized monolith to Kubernetes
   - Verify accessibility via Ingress
   - Test auto-scaling (load test to trigger HPA)

### Phases 1-4: Service Extraction
- Each extracted service deployed as Kubernetes Deployment + Service
- Ingress rules updated to route traffic to new services
- Feature flags control traffic routing (see Migration-Plan.md)

---

## Success Criteria

- ✅ Kubernetes cluster operational with 99.9%+ uptime
- ✅ All services deployed and accessible via Ingress
- ✅ Auto-scaling triggers correctly under load
- ✅ Zero-downtime deployments validated (rolling updates)
- ✅ Rollback procedure tested and documented
- ✅ Team trained on Kubernetes fundamentals
- ✅ Monitoring dashboards operational (Grafana)

---

## References

- [Kubernetes Official Documentation](https://kubernetes.io/docs/)
- [Azure Kubernetes Service (AKS)](https://learn.microsoft.com/en-us/azure/aks/)
- [Kubernetes Best Practices](https://kubernetes.io/docs/concepts/configuration/overview/)
- [Target-Architecture.md](./Target-Architecture.md) - Deployment model details
- [Migration-Plan.md](./Migration-Plan.md) - Phase 0 infrastructure setup

---

## Notes

- **Security:** Enable RBAC, network policies, and pod security standards in production
- **Cost Optimization:** Monitor cluster costs monthly, right-size resources
- **Upgrades:** Plan Kubernetes version upgrades quarterly (managed services auto-upgrade control plane)
- **Disaster Recovery:** Document cluster recreation procedure from backups

---

**Review Cycle:** Annually or after major Kubernetes version upgrades  
**Next Review:** After Phase 0 completion (infrastructure validation)
