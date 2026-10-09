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

// The (name, projectPath) overload takes a plain path string, which survives sourceName
// substitution as text — unlike AddProject<DKNet.StaticData_Api>, whose generated Projects.* identifier
// (derived from the .csproj file name with '.'/'-' replaced by '_') can disagree with the
// template engine's own text substitution for a name containing a dot (e.g. "DKNet.Accounts").
var api = builder.AddProject("Api", "../DKNet.StaticData.Api/DKNet.StaticData.Api.csproj")
    .WithReference(apDb, "AppDb")
    .WithEnvironment("BlobStorage__Provider", "Local")
    .WithEnvironment("BlobStorage__LocalFolder__RootFolder", blobFolder)
    .WaitFor(apDb);
if (useSqlServer)
{
    api.WithEnvironment("Database__Provider", "SqlServer");
}

await builder.Build().RunAsync();
