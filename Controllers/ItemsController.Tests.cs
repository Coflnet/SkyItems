using System;
using Coflnet.Sky.Items.Models;
using Coflnet.Sky.Items.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Coflnet.Sky.Items.Controllers;

public class ItemsControllerTests
{
    [Test]
    public void SearchUsesIndexedModifierCandidateSubquery()
    {
        var options = new DbContextOptionsBuilder<ItemDbContext>()
            .UseMySql("server=localhost;user=test;password=secret;database=items", new MariaDbServerVersion(new Version(10, 5, 5)))
            .Options;
        using var context = new ItemDbContext(options);
        var storage = new ItemMetaStorage();
        var service = new ItemService(context, NullLogger<ItemService>.Instance, storage);
        var controller = new ItemsController(service, context, NullLogger<ItemsController>.Instance, storage);

        var sql = controller.GetSelectForQueryTerm("dragon").ToQueryString();

        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("`i`.`Id` IN ("));
            Assert.That(sql, Does.Not.Contain("INNER JOIN `Items`"));
        });
    }
}
