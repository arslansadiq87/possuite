using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pos.Server.Api;     // your DbContext namespace
             // for Npgsql types (optional, but good to have)

public class ServerDesignTimeFactory : IDesignTimeDbContextFactory<ServerDbContext>
{
    public ServerDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("DT_PG_CS")
                 ?? "Host=127.0.0.1;Port=5432;Database=posserver;Username=posapp;Password=Strong#Pass1;Pooling=true";

        var b = new DbContextOptionsBuilder<ServerDbContext>()
            .UseNpgsql(cs);

        return new ServerDbContext(b.Options);
    }
}

