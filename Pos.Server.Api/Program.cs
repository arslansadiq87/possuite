// Pos.Server.Api/Program.cs
using Microsoft.EntityFrameworkCore;
using Pos.Persistence; // <-- AppDbContext lives here

var builder = WebApplication.CreateBuilder(args);

// 1) DbContext from ConnectionStrings:Default (appsettings.Development.json)
builder.Services.AddDbContext<PosClientDbContext>(opts =>
    opts.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// 2) OpenAPI/Swagger (handy during dev)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 3) CORS (open for now; restrict in prod)
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PosClientDbContext>();
    await db.Database.MigrateAsync(); 
    //DbSeeder.Seed(db);
}

// 4) Dev-time Swagger UI
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// 5) Middlewares
app.UseHttpsRedirection();
app.UseCors();

// 6) Health check
app.MapGet("/health", () => Results.Ok(new { ok = true, t = DateTime.UtcNow }));

// 7) Your existing weather endpoint (kept intact)
var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
