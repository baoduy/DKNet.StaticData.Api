using Npgsql;

void Try(string label, string cs)
{
    try
    {
        var b = new NpgsqlConnectionStringBuilder(cs);
        Console.WriteLine($"{label}: Timeout={b.Timeout} ConnString='{b.ConnectionString}'");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{label}: THROWS {ex.GetType().Name}: {ex.Message}");
    }
}

Try("Timeout=-1", "Host=10.255.255.1;Port=5432;Database=staticdata;Username=svc;Password=secret;Timeout=-1");
Try("Timeout=0", "Host=10.255.255.1;Port=5432;Database=staticdata;Username=svc;Password=secret;Timeout=0");

// password round trip with special chars
Try("password with special chars", "Host=10.255.255.1;Port=5432;Database=staticdata;Username=svc;Password=p@ss;word=123;Timeout=2");

var b2 = new NpgsqlConnectionStringBuilder("Host=10.255.255.1;Port=5432;Database=staticdata;Username=svc;Password=p@ss'w\"ord=1,2;Timeout=2");
Console.WriteLine($"password roundtrip: Password='{b2.Password}' ConnString='{b2.ConnectionString}'");
b2.Timeout = 4;
var b3 = new NpgsqlConnectionStringBuilder(b2.ConnectionString);
Console.WriteLine($"after cap: Password='{b3.Password}' matches original: {b3.Password == b2.Password}");

// command timeout vs connect timeout keys present together
Try("with CommandTimeout and Pooling keys", "Host=10.255.255.1;Port=5432;Database=staticdata;Username=svc;Password=secret;Timeout=15;CommandTimeout=30;Pooling=true;Maximum Pool Size=5");
