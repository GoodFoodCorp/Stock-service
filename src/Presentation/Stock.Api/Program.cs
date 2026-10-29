using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Formatting.Compact;
using Stock.Api.Middleware;
using Stock.Application;
using Stock.Infrastructure;
using Stock.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Structured JSON logs (Serilog)
builder.Host.UseSerilog((_, cfg) => cfg
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("service", "stock-service")
    .WriteTo.Console(new CompactJsonFormatter()));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers();

// JWT validation — HS256 shared secret issued by auth-service (see root README).
var jwtSecret = builder.Configuration["JWT_SECRET"]
    ?? throw new InvalidOperationException("JWT_SECRET is required.");
var jwtIssuer = builder.Configuration["JWT_ISSUER"] ?? "goodfood-auth";
var jwtAudience = builder.Configuration["JWT_AUDIENCE"] ?? "goodfood-api";

// Shared secret for service-to-service calls on /internal/... (X-Service-Key header).
if (string.IsNullOrWhiteSpace(builder.Configuration["SERVICE_API_KEY"]))
    throw new InvalidOperationException("SERVICE_API_KEY is required.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false; // keep raw claim names (sub, tenant_id, role_slugs)
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = "role_slugs",
            NameClaimType = "sub",
        };
        options.Events = new JwtBearerEvents
        {
            // Web clients authenticate with the HttpOnly cookie set by auth-service.
            OnMessageReceived = ctx =>
            {
                if (string.IsNullOrEmpty(ctx.Token) &&
                    ctx.Request.Cookies.TryGetValue("auth_token", out var cookie))
                {
                    ctx.Token = cookie;
                }

                return Task.CompletedTask;
            },
            // Only access tokens open the API: refresh tokens (typ=refresh,
            // signed with another key anyway) and untyped tokens are refused.
            OnTokenValidated = ctx =>
            {
                if (ctx.Principal?.FindFirst("typ")?.Value != "access")
                {
                    ctx.Fail("Not an access token.");
                }

                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Stock Service API",
        Version = "v1",
        Description = "Good Food 3.0 — franchisee stock & replenishment management.",
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
            },
            Array.Empty<string>()
        },
    });
});

builder.Services.AddHealthChecks().AddDbContextCheck<StockDbContext>("database");

var app = builder.Build();

// Apply EF Core migrations at startup (retry while the DB container boots).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<StockDbContext>();
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await LockedMigrator.MigrateAsync(db);
            break;
        }
        catch (Exception ex) when (attempt < 15)
        {
            app.Logger.LogWarning("Database not ready (attempt {Attempt}/15): {Error}", attempt, ex.Message);
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}

app.UseMiddleware<RequestIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseSerilogRequestLogging();

app.UseSwagger(c => c.RouteTemplate = "docs/{documentName}/openapi.json");
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/docs/v1/openapi.json", "Stock Service v1");
    c.RoutePrefix = "docs";
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/readyz");
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();
