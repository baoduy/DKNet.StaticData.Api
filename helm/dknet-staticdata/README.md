# dknet-staticdata

Slice 1 deploys only the API, using drunk-app 2.0.2. Chart version: 0.1.0.
The dependency is pinned and Chart.lock is committed. No database is installed.
Blob providers and file allow-list configuration arrive with slice 2.

## Database secret delivery

Choose an existing Kubernetes Secret, populated by the deployment's secret store.
The operator creates `staticdata-database` in the release namespace with the data key
`ConnectionStrings__AppDb`. `api.secretFrom` references its name. Kubernetes supplies
its keys through `envFrom.secretRef`; .NET maps double underscores to section separators,
so the unchanged API reads `ConnectionStrings:AppDb`. No credential enters Helm values,
a ConfigMap, chart release metadata, app settings, or the image.

The Secret must exist before the pod starts. For Key Vault, the operator's existing
secret synchronizer must create it. This chart needs no CSI driver, workload identity,
service-account token, or SecretProviderClass. It avoids the Accounts chart's missing
`clientID` field entirely. Kubernetes Secret delivery is the selected alternative to a
CSI file mount. Restrict Secret access and encrypt the cluster's Secret store at rest.
Rotate through the secret store, then restart pods: environment values do not refresh
inside a running process. The chart does not authenticate or configure the synchronizer.

`python3 .github/scripts/prove-secret-config.py` checks the rendered reference and supplies
the same Secret data key to the built API. A disposable Postgres container proves startup
migrations and `/healthz` returning `{"status":"Healthy"}`. CI repeats that proof.
This is a local render and application proof; no Kubernetes deployment is performed.

## Values

| Value | Default | Purpose |
|---|---|---|
| `global` | `{}` | Parent global map for Helm dependency merging. |
| `api.enabled` | `true` | Enable the drunk-app dependency. |
| `api.nameOverride` | `staticdata-api` | Component naming through library helpers; release name scopes resources. |
| `api.global.image` | `ghcr.io/baoduy/dknet.staticdata-api` | Published API image. |
| `api.global.tag` | `latest` | Use a release tag for reproducible deployments. |
| `api.global.imagePullPolicy` | `IfNotPresent` | Image pull policy. |
| `api.deployment.enabled` | `true` | Render the Deployment. |
| `api.deployment.replicaCount` | `1` | API replicas. |
| `api.deployment.ports.http` | `8080` | Container HTTP port. |
| `api.deployment.liveness`, `api.deployment.readiness` | `/healthz` | Anonymous status-only probes. |
| `api.service.type` | `ClusterIP` | Internal service; port 80 targets HTTP 8080. |
| `api.configMap.Database__Provider` | `Postgres` | Choose `Postgres` or `SqlServer`; supply matching AppDb credentials. |
| `api.configMap.FeatureManagement__RequireAuthorization` | `true` | Keep token checks enabled in deployments. |
| `api.configMap.FeatureManagement__EnableDemoAuthentication` | `false` | Keep demonstration sign-in disabled. |
| `api.configMap.FeatureManagement__EnableHealthCheck` | `true` | Register health routes. |
| `api.configMap.FeatureManagement__EnableHttps` | `false` | TLS ends before the pod. |
| `api.configMap.FeatureManagement__EnableAzureAppConfig` | `false` | Environment configuration is authoritative. |
| `api.configMap.FeatureManagement__RunDbMigrationWhenAppStart` | `true` | Apply the selected database migrations on startup. |
| `api.configMap.ASPNETCORE_ENVIRONMENT` | `Production` | Load production settings. |
| `api.configMap.ASPNETCORE_HTTP_PORTS` | `8080` | Bind container HTTP port. |
| `api.configMap.Authentication__Schemes__Bearer__MetadataAddress` | Tenant placeholder | Set the deployment's OIDC discovery URL. |
| `api.configMap.Authentication__Schemes__Bearer__ValidAudiences__0` | Client placeholder | Set the API audience. |
| `api.configMap.Authentication__Schemes__Bearer__ValidIssuer` | Tenant placeholder | Set the deployment's token issuer. |
| `api.secretFrom` | `[staticdata-database]` | Existing namespace Secret names; never put Secret content in values. |
| `api.secretProvider.enabled` | `false` | Disable the incomplete upstream Key Vault CSI path. |
| `api.serviceAccount.enabled` | `false` | No cloud identity needed by this chart. |
| `api.podSecurityContext` | UID/GID/fsGroup 1654, non-root, RuntimeDefault | Match the SDK image user. |
| `api.securityContext` | Read-only root, no escalation, drop ALL | Container restrictions. |
| `api.resources` | Requests 100m/128Mi; limits 500m/256Mi | Explicit runtime budget. |
| `api.volumes.tmp` | Writable emptyDir at `/tmp` | Runtime temporary files on a read-only root. |
| `api.httpRoute.enabled` | `false` | Opt into a Gateway API route. |
| `api.httpRoute.parentRefs` | `[]` | Existing Gateway references when enabled. |
| `api.httpRoute.hostnames` | `[]` | Route hostnames when enabled. |

## Verification

From the repository root, with Helm and helm-unittest 1.2.1 installed:

```sh
bash .github/scripts/verify-chart.sh
python3 .github/scripts/prove-secret-config.py
```

The second command needs Python with PyYAML 6.0.3, Docker, .NET 10, and a Release build.
Unit assertions cover configuration, Secret references, probes, internal Service,
and the disabled/enabled HTTPRoute. No reusable library templates are copied here.

The image publish workflow overrides the scaffold's Alpine base with `aspnet:10.0`,
matching the requested glibc `linux-x64` and `linux-arm64` RIDs. It runs only on `main`;
this chart consumes its release version or `latest`. Chart publishing is not added in slice 1.
