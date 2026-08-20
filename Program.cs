using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using SmartHomeIoT.Api.Data;
using SmartHomeIoT.Api.Middleware;
using SmartHomeIoT.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------
// Services
// ---------------------------------------------------------------------

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Keep enums (e.g. DeviceStatus) readable in JSON instead of raw integers.
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Smart Home IoT Platform API",
        Version = "v1",
        Description =
            "REST API for the Smart Home IoT Platform (Raspberry Pi hub). " +
            "Manages rooms, devices, sensor data and the system event log, mapped directly onto the " +
            "existing MySQL schema (Room, Device, SensorData, EventLog)."
    });

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
});

// EF Core mapped onto the existing MySQL database (Pomelo provider).
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' is not configured. Set it in appsettings.json, an environment variable, or user-secrets.");

var autoDetectVersion = builder.Configuration.GetValue<bool>("Database:AutoDetectServerVersion", true);
var configuredVersion = builder.Configuration["Database:MySqlServerVersion"];

var serverVersion = autoDetectVersion
    ? ServerVersion.AutoDetect(connectionString)
    : ServerVersion.Parse(configuredVersion ?? "8.0.36-mysql");

builder.Services.AddDbContext<SmartHomeDbContext>(options =>
    options.UseMySql(connectionString, serverVersion));

builder.Services.AddScoped<ISensorValidationService, SensorValidationService>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<SmartHomeDbContext>("database");

builder.Services.AddScoped<SensorDataService>();
builder.Services.AddHostedService<MqttService>();
builder.Services.AddScoped<WifiDiscoveryService>();

builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
    options.AddPolicy("Dashboard", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// ---------------------------------------------------------------------
// Optional: apply pending EF Core migrations on startup (dev convenience).
// Since this API is meant to map onto an ALREADY EXISTING database, this is
// off by default in production - see appsettings.json "Database:ApplyMigrationsOnStartup".
// ---------------------------------------------------------------------
if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<SmartHomeDbContext>();
    db.Database.Migrate();
}
else if (app.Configuration.GetValue<bool>("Database:EnsureCreatedIfNoMigrations"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<SmartHomeDbContext>();
    db.Database.EnsureCreated();
}

// ---------------------------------------------------------------------
// Middleware pipeline
// ---------------------------------------------------------------------

app.UseMiddleware<ExceptionHandlingMiddleware>();

// Swagger is available in all environments so the API is easy to explore on the local network;
// remove the `|| true` guard below if you'd rather restrict it to Development only.
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Smart Home IoT Platform API v1");
    options.RoutePrefix = "swagger"; // browse at /swagger
});

app.UseHttpsRedirection();
app.UseCors("Dashboard");
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

// Simple root redirect so hitting the hub's base URL lands somewhere useful.
app.MapGet("/", () => Results.Redirect("/swagger"));

app.Run();
