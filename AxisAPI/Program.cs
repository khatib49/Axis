using Application;
using Application.Services;
using Application.Services.SignalR;
using AxisAPI.Utils;
using Domain.Identity;
using Hangfire;
using Hangfire.PostgreSql;
using Infrastructure;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NpgsqlTypes;
using Serilog;
using Serilog.Sinks.PostgreSQL;
using System.Text;
using Application.Middleware;
using AxisAPI.Middleware;

// ---- Column writers (root namespace, no dataLength) ----
var pgColumns = new Dictionary<string, ColumnWriterBase>
{
    // basic columns
    ["timestamp"] = new TimestampColumnWriter(NpgsqlDbType.TimestampTz),
    ["level"] = new LevelColumnWriter(true, NpgsqlDbType.Varchar),        // renderAsText = true
    ["message"] = new RenderedMessageColumnWriter(NpgsqlDbType.Text),
    ["exception"] = new ExceptionColumnWriter(NpgsqlDbType.Text),

    // your pushed properties (from LogContext in the attribute)
    ["req_id"] = new SinglePropertyColumnWriter("ReqId", PropertyWriteMethod.ToString, NpgsqlDbType.Varchar),
    ["user_name"] = new SinglePropertyColumnWriter("UserName", PropertyWriteMethod.ToString, NpgsqlDbType.Text),
    ["controller"] = new SinglePropertyColumnWriter("Controller", PropertyWriteMethod.ToString, NpgsqlDbType.Text),
    ["action"] = new SinglePropertyColumnWriter("Action", PropertyWriteMethod.ToString, NpgsqlDbType.Text),
    ["method"] = new SinglePropertyColumnWriter("Method", PropertyWriteMethod.ToString, NpgsqlDbType.Varchar),
    ["path"] = new SinglePropertyColumnWriter("Path", PropertyWriteMethod.ToString, NpgsqlDbType.Text),
    ["status_code"] = new SinglePropertyColumnWriter("StatusCode", PropertyWriteMethod.Raw, NpgsqlDbType.Integer),
    ["elapsed_ms"] = new SinglePropertyColumnWriter("ElapsedMs", PropertyWriteMethod.Raw, NpgsqlDbType.Integer),

    // previews
    ["args_json"] = new SinglePropertyColumnWriter("Args", PropertyWriteMethod.Json, NpgsqlDbType.Jsonb),
    ["result_preview"] = new SinglePropertyColumnWriter("ResultPreview", PropertyWriteMethod.ToString, NpgsqlDbType.Text),

    // full event as JSON
    ["properties"] = new LogEventSerializedColumnWriter(NpgsqlDbType.Jsonb),
};

// ---- Bootstrap Serilog (top of Program.cs, before builder) ----
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    // comment the following 3 lines if the packages aren’t installed yet:
    // .Enrich.WithMachineName()  // Serilog.Enrichers.Environment
    // .Enrich.WithProcessId()    // Serilog.Enrichers.Process
    // .Enrich.WithThreadId()     // Serilog.Enrichers.Thread
    .WriteTo.Console()
    .CreateLogger();
var builder = WebApplication.CreateBuilder(args);


builder.Host.UseSerilog((ctx, sp, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.FromLogContext()
       .WriteTo.Console()
       .WriteTo.PostgreSQL(
            ctx.Configuration.GetConnectionString("Postgres"), // connectionString
            "error_logs",                                      // tableName
            pgColumns,                                         // column writers
            Serilog.Events.LogEventLevel.Warning               // restrictedToMinimumLevel
        );
});

builder.Services.AddInfrastructure(builder.Configuration); // DbContext + repos
builder.Services.AddApplication();                         // <-- registers IAuthService & IGameService
builder.Services.AddScoped<IImageStorageService, LocalImageStorageService>();


// Program.cs
builder.Services.AddHangfire(config =>
    config.UseSimpleAssemblyNameTypeSerializer()
          .UseRecommendedSerializerSettings()
          .UsePostgreSqlStorage(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddHangfireServer();

builder.Services.AddSignalR();


// Swagger services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Axis API", Version = "v1" });

    // JWT bearer in Swagger UI
    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter: Bearer {your JWT}",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };
    c.AddSecurityDefinition("Bearer", scheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement { { scheme, Array.Empty<string>() } });
});

// EF Core + PostgreSQL
//builder.Services.AddDbContext<ApplicationDbContext>(opt =>
//{
//    opt.UseNpgsql(builder.Configuration.GetConnectionString("Postgres"));
//    //opt.UseSnakeCaseNamingConvention(); // works now because we installed the naming conventions package
//});

// Identity
builder.Services
    .AddIdentityCore<AppUser>(o =>
    {
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.User.RequireUniqueEmail = true;
    })
    .AddRoles<AppRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>() // works after packages/fixes
    .AddDefaultTokenProviders();

// JWT
var jwt = builder.Configuration.GetSection("Jwt");
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new()
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = key,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();


builder.Services.AddCors(options => options.AddPolicy("CorsPolicy",
                option =>
                {
                    option
                    .WithOrigins("http://localhost:5173", "https://lively-pond-098449403.2.azurestaticapps.net", "https://www.axislb.com")
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials();
                }));



var app = builder.Build();

app.UseStaticFiles();


// 🔧 Serve Swagger ALWAYS (dev & prod)
// (Move these OUTSIDE any if (app.Environment.IsDevelopment()) block)

app.MapGet("/healthz", () => Results.Ok("OK"));

app.UseSwagger();
app.UseSwaggerUI(ui =>
{
    ui.SwaggerEndpoint("/swagger/v1/swagger.json", "Axis API v1");
    // ui.RoutePrefix = string.Empty; // <— uncomment if you want Swagger at root "/"
});

// your hub (below)
app.MapHub<ReceptionHub>("/hubs/reception");

app.UseHangfireDashboard("/hangfire");

app.UseHttpsRedirection();
app.UseForce403ForUnauthorized();
app.UseSerilogRequestEnricher();
app.UseAuthentication();
app.UseAuthorization();

app.UseCors("CorsPolicy");
app.MapControllers();


// Auto-create/upgrade the DB schema on boot (creates DB *schema* if DB exists)
//using (var scope = app.Services.CreateScope())
//{
//    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
//    await db.Database.MigrateAsync();
//}

app.Run();

