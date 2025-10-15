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
using System.Text;



var builder = WebApplication.CreateBuilder(args);


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
                    .WithOrigins("http://localhost:5173")
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials();
                }));



var app = builder.Build();

app.UseStaticFiles();


// 🔧 Serve Swagger ALWAYS (dev & prod)
// (Move these OUTSIDE any if (app.Environment.IsDevelopment()) block)
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

