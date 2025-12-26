using calc_server;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => { options.ListenAnyIP(8496); });
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.DefaultIgnoreCondition =
        System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});
// 2. Add Postgres
var postgresConn = builder.Configuration.GetConnectionString("PostgresConnection");
builder.Services.AddDbContext<CalculatorDbContext>(options => 
    options.UseNpgsql(postgresConn)
        .EnableSensitiveDataLogging()
        .EnableDetailedErrors());

// 3. Add MongoDB Client
var mongoConn = builder.Configuration.GetConnectionString("MongoConnection");
builder.Services.AddSingleton<IMongoClient>(new MongoClient(mongoConn));

// Use only AddLog4Net for logging
builder.Logging.ClearProviders();
builder.Logging.AddLog4Net("log4net.config");

var app = builder.Build();

app.UseMiddleware<RequestLoggingMiddleware>();

app.MapControllers();

app.Run();