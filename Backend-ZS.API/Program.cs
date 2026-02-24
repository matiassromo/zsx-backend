using System.Text;
using System.Text.Json.Serialization;
using Backend_ZS.API.Data;
using Backend_ZS.API.Mappings;
using Backend_ZS.API.Models.Domain;
using Backend_ZS.API.Repositories;
using Backend_ZS.API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// --- CORS: permitir llamadas desde el front ---
var corsPolicy = "_nextCors";
builder.Services.AddCors(options =>
{
    options.AddPolicy(corsPolicy, p =>
        p.WithOrigins(
            "http://localhost:3000",
            "https://localhost:3000",
            "https://zsx-frontend-k1wm.onrender.com"
        )
        .AllowAnyHeader()
        .AllowAnyMethod()
    // .AllowCredentials()
    );
});

// MVC
builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new DateOnlyJsonConverter());
        o.JsonSerializerOptions.Converters.Add(new TimeOnlyJsonConverter());
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Backend-ZS.API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// DbContext
builder.Services.AddDbContext<ZsDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("ZSConnectionString")));

// Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 6;
    options.User.RequireUniqueEmail = false;
})
.AddEntityFrameworkStores<ZsDbContext>()
.AddDefaultTokenProviders();

// JWT Authentication
var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY")
    ?? builder.Configuration["JwtSettings:Key"]
    ?? throw new InvalidOperationException("JWT Key is not configured");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["JwtSettings:Issuer"],
        ValidAudience = builder.Configuration["JwtSettings:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.Zero
    };
});

// AutoMapper
builder.Services.AddAutoMapper(cfg => { }, typeof(AutoMapperProfiles));

// Repos
builder.Services.AddScoped<IClientRepository, SqlClientRepository>();
builder.Services.AddScoped<IBarProductRepository, SqlBarProductRepository>();
builder.Services.AddScoped<IPaymentRepository, SqlPaymentRepository>();
builder.Services.AddScoped<ITransactionRepository, SqlTransactionRepository>();
builder.Services.AddScoped<IBarOrderRepository, SqlBarOrderRepository>();
builder.Services.AddScoped<IKeyRepository, SqlKeyRepository>();
builder.Services.AddScoped<IAccessCardRepository, SqlAccessCardRepository>();
builder.Services.AddScoped<IParkingRepository, SqlParkingRepository>();
builder.Services.AddScoped<IEntranceTransactionRepository, SqlEntranceTransactionRepository>();
builder.Services.AddScoped<IEntranceAccessCardRepository, SqlEntranceAccessCardRepository>();

// Services
builder.Services.AddScoped<IBarOrderService, SqlBarOrderService>();
builder.Services.AddScoped<ITokenService, TokenService>();

var app = builder.Build();

// Seed admin user password on startup (since EF HasData can't use PasswordHasher dynamically)
try
{
    using var scope = app.Services.CreateScope();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var adminUser = await userManager.FindByNameAsync("adminzs");
    if (adminUser != null && !await userManager.CheckPasswordAsync(adminUser, "admin123"))
    {
        var token = await userManager.GeneratePasswordResetTokenAsync(adminUser);
        await userManager.ResetPasswordAsync(adminUser, token, "admin123");
    }
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogWarning(ex, "Seeding skipped: could not connect to database on startup.");
}

// Seed demo user for portfolio visibility
try
{
    using var scope = app.Services.CreateScope();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var demoUser = await userManager.FindByNameAsync("demo");
    if (demoUser == null)
    {
        demoUser = new ApplicationUser
        {
            UserName = "demo",
            Email = "demo@zs.com",
            EmailConfirmed = true,
        };
        await userManager.CreateAsync(demoUser, "demo123");
        await userManager.AddToRoleAsync(demoUser, "admin");
    }
    else if (!await userManager.CheckPasswordAsync(demoUser, "demo123"))
    {
        var token = await userManager.GeneratePasswordResetTokenAsync(demoUser);
        await userManager.ResetPasswordAsync(demoUser, token, "demo123");
    }
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogWarning(ex, "Demo user seeding skipped.");
}


// ✅ Swagger habilitado SIEMPRE (Production incluido)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Backend-ZS.API v1");
    c.RoutePrefix = "swagger"; // https://zs-backend-api-fbb7ftdzfxd3bqh9.canadacentral-01.azurewebsites.net/swagger
});


if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

// --- activar CORS antes de MapControllers ---
app.UseCors(corsPolicy);

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
