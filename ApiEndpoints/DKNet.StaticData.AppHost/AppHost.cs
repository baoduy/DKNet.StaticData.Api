var builder = DistributedApplication.CreateBuilder(args);

// The database the API runs on: "Postgres" (default) or "SqlServer". Switch it here, or per run with
// `--Database:Provider SqlServer` (or the Database__Provider environment variable).
var database = builder.Configuration["Database:Provider"] ?? "Postgres";
var useSqlServer = string.Equals(database, "SqlServer", StringComparison.OrdinalIgnoreCase);

IResourceBuilder<IResourceWithConnectionString> apDb = useSqlServer
    ? builder.AddSqlServer("SqlServer").AddDatabase("AppDb")
    : builder.AddPostgres("Postgres").AddDatabase("AppDb");

// Uploaded files go to a local folder next to this AppHost (git-ignored), so no storage account is needed.
var blobFolder = Directory.CreateDirectory(Path.Combine(builder.AppHostDirectory, ".data", "blobs")).FullName;

// The local token issuer (ADR-0016): its realm defines the 2 app roles, maps them into the "roles" claim and gives
// its one caller client, dknet-staticdata-caller, both; it defines no scope for this service. The realm is
// re-imported from Realms/ at every start; with no data volume, anything changed in the admin screen is gone after a
// restart. The caller client's secret is committed in the realm file on purpose: it exists only here, and nothing
// deployed trusts this Keycloak. The port is fixed because the issuer address baked into every token must equal the
// API's ValidIssuer on every run; 8181, as DKNet.Accounts.Api's AppHost already takes 8180.
const string realm = "dknet-staticdata";
const string apiAudience = "dknet-staticdata-api";
const int keycloakPort = 8181;
var keycloakUser = builder.AddParameter("KeycloakAdminUser", "admin");
var keycloakPassword = builder.AddParameter("KeycloakAdminPassword", "admin", secret: true);
var keycloak = builder.AddKeycloak("Keycloak", keycloakPort, keycloakUser, keycloakPassword)
    .WithRealmImport("./Realms");
var realmUrl = ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/{realm}");

// The (name, projectPath) overload takes a plain path string, which survives sourceName
// substitution as text — unlike AddProject<DKNet.StaticData_Api>, whose generated Projects.* identifier
// (derived from the .csproj file name with '.'/'-' replaced by '_') can disagree with the
// template engine's own text substitution for a name containing a dot (e.g. "DKNet.Accounts").
var api = builder.AddProject("Api", "../DKNet.StaticData.Api/DKNet.StaticData.Api.csproj")
    .WithReference(apDb, "AppDb")
    .WithEnvironment("BlobStorage__Provider", "Local")
    .WithEnvironment("BlobStorage__LocalFolder__RootFolder", blobFolder)
    // Sign-in and role checks on, trusting only the local realm: signature, issuer, audience and lifetime are all
    // still checked. The one relaxation, fetching the realm's keys over plain HTTP, is set here and nowhere else.
    // appsettings.Development.json turns demo sign-in on; the API refuses to start with both on.
    .WithEnvironment("FeatureManagement__RequireAuthorization", "true")
    .WithEnvironment("FeatureManagement__EnableDemoAuthentication", "false")
    .WithEnvironment("Authentication__Schemes__Bearer__MetadataAddress",
        ReferenceExpression.Create($"{realmUrl}/.well-known/openid-configuration"))
    .WithEnvironment("Authentication__Schemes__Bearer__ValidIssuer", realmUrl)
    .WithEnvironment("Authentication__Schemes__Bearer__ValidAudiences__0", apiAudience)
    .WithEnvironment("Authentication__Schemes__Bearer__RequireHttpsMetadata", "false")
    .WaitFor(apDb)
    .WaitFor(keycloak);
if (useSqlServer)
{
    api.WithEnvironment("Database__Provider", "SqlServer");
}

await builder.Build().RunAsync();
