using calc_server;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => { options.ListenAnyIP(8496); });
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.DefaultIgnoreCondition =
        System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});

// Use only AddLog4Net for logging
builder.Logging.ClearProviders();
builder.Logging.AddLog4Net("log4net.config");

var app = builder.Build();

app.UseMiddleware<RequestLoggingMiddleware>();

app.MapControllers();

app.Run();