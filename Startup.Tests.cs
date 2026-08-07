using System.Collections.Generic;
using Coflnet.Sky.Items.Models;
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
}
