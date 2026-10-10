using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;
using DKNet.StaticData.Client;

namespace DKNet.StaticData.App.Tests.Client;

/// <summary>
/// Spec DRK-2206 §5 "Client package": the package a calling service references brings in no part of the service's
/// server code — no project reference, no server assembly reference, no server package dependency. Runs a real
/// <c>dotnet pack</c>.
/// </summary>
public sealed class PackagingTests
{
    private static readonly string[] ServerProjects =
    [
        "DKNet.StaticData.Api", "DKNet.StaticData.AppServices", "DKNet.StaticData.Domains", "DKNet.StaticData.Infra",
        "DKNet.StaticData.Infra.Postgres", "DKNet.StaticData.Infra.MsSql", "DKNet.StaticData.Share"
    ];

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DKNet.StaticData.sln")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new InvalidOperationException("No repository root above the test output.");
        }
    }

    private static string ClientProject =>
        Path.Combine(RepoRoot, "ApiEndpoints", "DKNet.StaticData.Client", "DKNet.StaticData.Client.csproj");

    /// <summary>Scenario: The typed client holds no server code.</summary>
    [Fact]
    public void TheTypedClientHoldsNoServerCode()
    {
        XDocument.Load(ClientProject).Descendants().Where(e => e.Name.LocalName == "ProjectReference").ShouldBeEmpty();
        typeof(IStaticDataFileClient).Assembly.GetReferencedAssemblies().Select(a => a.Name)
            .ShouldNotContain(name => ServerProjects.Contains(name));

        var nuspec = Pack();
        nuspec.Descendants().Where(e => e.Name.LocalName == "dependency")
            .Select(e => e.Attribute("id")?.Value ?? string.Empty)
            .ShouldNotContain(id => id.StartsWith("DKNet.StaticData.", StringComparison.Ordinal));
    }

    private static XDocument Pack()
    {
        var output = Path.Combine(Path.GetTempPath(), $"staticdata-client-pack-{Guid.NewGuid():N}");
        var start = new ProcessStartInfo("dotnet", $"pack \"{ClientProject}\" -c Release -o \"{output}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = Process.Start(start)!;
        var log = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        process.ExitCode.ShouldBe(0, $"dotnet pack failed: {log.Result}{errors.Result}");

        var package = Directory.GetFiles(output, "DKNet.StaticData.Client*.nupkg").Single();
        using var archive = ZipFile.OpenRead(package);
        using var nuspec = archive.Entries.Single(e => e.Name.EndsWith(".nuspec", StringComparison.Ordinal)).Open();
        return XDocument.Load(nuspec);
    }
}
