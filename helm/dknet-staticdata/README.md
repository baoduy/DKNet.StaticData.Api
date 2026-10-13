# dknet-staticdata

Slices 1 and 2 deploy the API, using drunk-app 2.0.2. Chart version: 0.1.1.
The dependency is pinned and Chart.lock is committed. No database is installed.
Slice 2 adds blob provider settings, the file allow-list, and writable file storage.

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

## Blob providers and secrets

Set `api.configMap.BlobStorage__Provider` to `Local`, `AzureStorage`, or `AwsS3`.
The ConfigMap uses exactly the section names read by DKNet's blob adapters.
Unused provider settings are ignored. Keep all credentials outside Helm values.

| Provider | Non-secret settings under `api.configMap` | Required existing Secret data keys |
|---|---|---|
| `Local` (default) | `BlobStorage__LocalFolder__RootFolder` | None beyond the database key. |
| `AzureStorage` | `BlobService__AzureStorage__ContainerName` | `BlobService__AzureStorage__ConnectionString` |
| `AwsS3` | `BlobService__S3__ConnectionString`, `BlobService__S3__BucketName`, `BlobService__S3__RegionEndpointName`, `BlobService__S3__ForcePathStyle` | `BlobService__S3__AccessKey`, `BlobService__S3__Secret` |

`BlobService__S3__ConnectionString` is the service URL, not a credential.
Use the regional S3 endpoint for the chosen region, or an S3-compatible HTTPS endpoint.
Set `ForcePathStyle` to `"true"` when the endpoint requires path-style addressing.
Never embed credentials in an endpoint URL.

Populate cloud credentials through the same secret synchronizer as the database.
Put the provider's keys into `staticdata-database`, or append another existing Secret
name to `api.secretFrom`, keeping the database Secret in the list. No credential
value belongs in a ConfigMap, chart defaults, or a Helm override file.
Restart pods after credential rotation, as with database credentials.

For example, an Azure deployment can use this credential-free override file:

```yaml
api:
  configMap:
    BlobStorage__Provider: AzureStorage
    BlobService__AzureStorage__ContainerName: documents
  secretFrom: [staticdata-database, staticdata-azure]
  volumes:
    files:
      emptyDir: true
  deployment:
    strategy:
      type: RollingUpdate
```

The operator-managed `staticdata-azure` Secret supplies the Azure connection string.
For S3, select `AwsS3`, configure its endpoint and bucket, and reference the Secret
containing its access key and secret. Setting `api.volumes.files.emptyDir: true`
avoids provisioning an unused PVC for cloud deployments; the unused mount remains.
Keep `/tmp` writable for every provider.

## Local storage and upload space

Local files live at `/var/lib/staticdata`, mounted from the `staticdata-api-files`
PVC. Its default size is 10Gi, with `ReadWriteOnce` and the cluster's default
StorageClass. Choose a StorageClass supporting the configured access mode.
The pod's UID/GID and fsGroup are 1654; the volume must be writable by that user.
Change the root folder and volume mount path together if another path is needed.

`Local` is for 1 replica only, unless every replica shares the volume.
The default `Recreate` update strategy avoids overlapping Local writers during updates,
and causes brief downtime. Multiple replicas need shared storage with `ReadWriteMany`
and a suitable StorageClass, or a cloud provider. A cloud provider can use
`RollingUpdate`. The root filesystem stays read-only, and Local file bytes persist
across pod replacement. Monitor volume capacity; stored files have no automatic expiry.

Uploads can buffer 51,000,000 bytes each to `/tmp`.
The rendered volume is `emptyDir: {}` with no `medium: Memory`.
It is disk-backed and does not charge upload buffers to the 256Mi memory limit;
[memory-backed emptyDir would count against memory](https://kubernetes.io/docs/concepts/storage/volumes/#emptydir).
The container requests 2Gi and limits 4Gi of ephemeral storage. The request covers
20 concurrent maximum-size buffers (1,020,000,000 bytes), with room for runtime files
and logs. Increase the storage budget if upload concurrency grows.
`drunk-app` 2.0.2 renders no per-volume `sizeLimit`; the container's ephemeral-storage
limit covers `/tmp`, writable layers, and logs, rather than reserving 4Gi just for `/tmp`.
[Kubernetes accounts these uses together](https://kubernetes.io/docs/concepts/configuration/manage-resources-containers/#local-ephemeral-storage).
Nodes must have sufficient disk and supported ephemeral-storage accounting.

## File type allow-list

The defaults match the design's 14 extensions: `.pdf`, `.png`, `.jpg`, `.jpeg`, `.gif`,
`.txt`, `.csv`, `.json`, `.xml`, `.doc`, `.docx`, `.xls`, `.xlsx`, and `.zip`.
`api.configMap.Files__AllowedExtensions__0` through `__13` bind the .NET array.
Entries include the dot. The application trims whitespace and normalizes case.

Override the indexed values to change the allow-list. To narrow it, set every unused
default index to `""`; the application ignores blank entries. Do not use YAML `null`:
the dependency can render it as a null ConfigMap value. For a PDF-only deployment:

```yaml
api:
  configMap:
    Files__AllowedExtensions__0: .pdf
    Files__AllowedExtensions__1: ""
    Files__AllowedExtensions__2: ""
    Files__AllowedExtensions__3: ""
    Files__AllowedExtensions__4: ""
    Files__AllowedExtensions__5: ""
    Files__AllowedExtensions__6: ""
    Files__AllowedExtensions__7: ""
    Files__AllowedExtensions__8: ""
    Files__AllowedExtensions__9: ""
    Files__AllowedExtensions__10: ""
    Files__AllowedExtensions__11: ""
    Files__AllowedExtensions__12: ""
    Files__AllowedExtensions__13: ""
```

Add indices starting at `__14` to extend the list. At least 1 nonblank extension
is required; an entirely blank allow-list stops application startup.

## Entra ID app roles

Define 2 app roles on the API's Entra ID app registration, allowed for **Applications**:
`staticdata.read` and `staticdata.write`. Keep **Assignment required** on for the API's
enterprise application. Assign each calling app the roles it needs and grant admin
consent for its application permissions.

Every `/v1` GET needs `staticdata.read`; POST, PUT, and DELETE need `staticdata.write`.
Write does not include read, so an app doing both needs both assignments.
Client-credentials tokens carry these values in the `roles` claim.
The role names are fixed in application code, not chart values. A valid token without
the required role receives 403. The health endpoints remain anonymous.

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
| `api.deployment.strategy.type` | `Recreate` | Avoid overlapping pods with Local storage; cloud deployments can select `RollingUpdate`. |
| `api.deployment.ports.http` | `8080` | Container HTTP port. |
| `api.deployment.liveness`, `api.deployment.readiness` | `/healthz` | Anonymous status-only probes. |
| `api.service.type` | `ClusterIP` | Internal service; port 80 targets HTTP 8080. |
| `api.configMap.Database__Provider` | `Postgres` | Choose `Postgres` or `SqlServer`; supply matching AppDb credentials. |
| `api.configMap.BlobStorage__Provider` | `Local` | Choose `Local`, `AzureStorage`, or `AwsS3`. |
| `api.configMap.BlobStorage__LocalFolder__RootFolder` | `/var/lib/staticdata` | Writable Local folder; match the file volume mount path. |
| `api.configMap.BlobService__AzureStorage__ContainerName` | `staticdata` | Azure blob container. |
| `api.configMap.BlobService__S3__ConnectionString` | `https://s3.amazonaws.com` | Credential-free S3 service endpoint URL. |
| `api.configMap.BlobService__S3__BucketName` | `staticdata` | S3 bucket. |
| `api.configMap.BlobService__S3__RegionEndpointName` | `us-east-1` | S3 region. |
| `api.configMap.BlobService__S3__ForcePathStyle` | `false` | Use path-style addressing if the endpoint requires it. |
| `api.configMap.Files__AllowedExtensions__0` through `__13` | Design's 14 extensions | Indexed allow-list; blank unused entries when narrowing. |
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
| `api.secretFrom` | `[staticdata-database]` | Existing database and blob credential Secret names; never put Secret content in values. |
| `api.secretProvider.enabled` | `false` | Disable the incomplete upstream Key Vault CSI path. |
| `api.serviceAccount.enabled` | `false` | No cloud identity needed by this chart. |
| `api.podSecurityContext` | UID/GID/fsGroup 1654, non-root, RuntimeDefault | Match the SDK image user. |
| `api.securityContext` | Read-only root, no escalation, drop ALL | Container restrictions. |
| `api.resources` | Requests 100m/128Mi/2Gi disk; limits 500m/256Mi/4Gi disk | CPU, memory, and ephemeral-storage budget. |
| `api.volumes.files.mountPath` | `/var/lib/staticdata` | Writable Local file volume mount. |
| `api.volumes.files.emptyDir` | `false` | Persistent Local storage; use `true` to avoid a PVC for cloud providers. |
| `api.volumes.files.readOnly` | `false` | Allow file writes. |
| `api.volumes.files.size` | `10Gi` | Local PVC requested capacity. |
| `api.volumes.files.accessMode` | `ReadWriteOnce` | Use `ReadWriteMany` with shared storage for multiple Local replicas. |
| `api.volumes.files.storageClassName` | `""` | Cluster default StorageClass; choose a suitable provisioner. |
| `api.volumes.tmp` | Writable disk-backed emptyDir at `/tmp` | Upload buffers and runtime temporary files on a read-only root. |
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
Unit assertions cover all providers, credential references, the default and narrowed
allow-list, persistent and shared Local storage, disk-backed temporary space, probes,
internal Service, and the disabled/enabled HTTPRoute. No library templates are copied here.

The image publish workflow overrides the scaffold's Alpine base with `aspnet:10.0`,
matching the requested glibc `linux-x64` and `linux-arm64` RIDs. It runs only on `main`;
this chart consumes its release version or `latest`. The same workflow packs
`DKNet.StaticData.Client` with that computed release version and pushes it to GitHub
Packages. The `dev` build still builds the client and runs its tests through the full
solution suite on PostgreSQL and SQL Server 2022. Chart publishing is not added here.
