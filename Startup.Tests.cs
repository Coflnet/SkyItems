using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Coflnet.Sky.Items.Models;
using Coflnet.Sky.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using NUnit.Framework;

namespace Coflnet.Sky.Items;

public class StartupTests
{
    [TestCase(null, 100u, 64u)]
    [TestCase("12", 100u, 12u)]
    [TestCase("32", 8u, 8u)]
    public void ConfigureServicesLimitsDatabaseConnections(string configuredLimit, uint connectionLimit, uint expectedLimit)
    {
        var values = new Dictionary<string, string>
        {
            ["DB_CONNECTION"] = $"server=localhost;user=test;password=secret;database=items;Pooling=False;Maximum Pool Size={connectionLimit}",
            ["MARIADB_VERSION"] = "10.5.5"
        };
        if (configuredLimit != null)
            values["DB_MAX_POOL_SIZE"] = configuredLimit;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();

        new Startup(configuration).ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var connectionString = scope.ServiceProvider.GetRequiredService<ItemDbContext>().Database.GetConnectionString();
        var options = new MySqlConnectionStringBuilder(connectionString);
        Assert.Multiple(() =>
        {
            Assert.That(options.Pooling, Is.True);
            Assert.That(options.MaximumPoolSize, Is.EqualTo(expectedLimit));
            Assert.That(options.Server, Is.EqualTo("localhost"));
            Assert.That(options.Database, Is.EqualTo("items"));
        });
    }
    [TestCase(false)]
    [TestCase(true)]
    public void ColdCopyKeepsItsDistinctDatabaseAndConnectionPolicy(bool tls)
    {
        var values = new Dictionary<string, string>
        {
            ["DB_CONNECTION"] = "Server=primary;Database=items;User ID=primary;Password=synthetic-only",
            ["DBCONNECTION"] = "Server=cold-a,cold-b;Port=3307;Database=legacy;User ID=cold;Password=synthetic-only;SslMode=None",
            ["MARIADB_VERSION"] = "10.5.5"
        };
        if (tls)
        {
            values["DB:TLS:CA_CERT_PATH"] = "roots.pem";
            values["DB:TLS:CERT_PATH"] = "client.pem";
            values["DB:TLS:KEY_PATH"] = "client.key";
        }
        var services = new ServiceCollection();
        new Startup(new ConfigurationBuilder().AddInMemoryCollection(values).Build()).ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<HypixelContext>();
        var options = new MySqlConnectionStringBuilder(context.Database.GetConnectionString());
        Assert.Multiple(() =>
        {
            Assert.That(options.Server, Is.EqualTo("cold-a,cold-b"));
            Assert.That(options.Port, Is.EqualTo(3307));
            Assert.That(options.Database, Is.EqualTo("legacy"));
            Assert.That(options.UserID, Is.EqualTo("cold"));
            Assert.That(options.SslMode, Is.EqualTo(tls ? MySqlSslMode.Required : MySqlSslMode.None));
            Assert.That(context.Database.GetCommandTimeout(), Is.EqualTo(60));
            Assert.That(context.GetService<IDbContextOptions>().Extensions.OfType<RelationalOptionsExtension>().Single().MaxBatchSize, Is.EqualTo(100));
        });
    }

}
