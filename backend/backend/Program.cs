using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using backend.Data;
using backend.Services;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using Stripe;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// Stripe API key
StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];

// Register business services (one instance per HTTP request: "scoped")
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ISubscriptionsService,SubscriptionsService>();
builder.Services.AddScoped<IFormService, FormService>();
builder.Services.AddScoped<IUserService, UserService>();

// JWT configuration. The key signs every token: an empty key would happily validate
// forged tokens, so the startup check below refuses to boot without one.
var jwtKey = builder.Configuration["Jwt:Key"] ?? string.Empty;
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
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
});

builder.Services.AddControllers();

// CORS: the React app runs on a different origin than the API (http://localhost:3000
// in development, the real domain in production). A browser blocks a cross-origin
// call unless the API explicitly allows it, so declare the list of trusted origins
// here. Override it without touching the code through the configuration key
// "Cors:AllowedOrigins" (appsettings.json, an environment variable, docker compose...).
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins")
    .GetChildren()
    .Select(child => child.Value)
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin!)
    .ToArray();

if (allowedOrigins.Length == 0) allowedOrigins = new[] { "http://localhost:3000" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// OpenAPI: generates the machine-readable description of every endpoint from the
// controllers and DTOs. Exposed in development only (see below).
// The document transformer below declares the Bearer token scheme so that the
// generated UI can send an "Authorization: Bearer <jwt>" header on protected calls.
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Paste the token returned by /api/auth/login or /api/auth/register."
        };
        document.Security ??= new List<OpenApiSecurityRequirement>();
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
        });
        return Task.CompletedTask;
    });
});

var app = builder.Build();

// Fail fast: a missing or too short signing key is a silent security hole,
// not something to discover in production logs. HMAC-SHA256 needs at least 32 bytes.
// Deliberately after builder.Build() so that "dotnet ef" keeps working with no key set.
if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key is missing or shorter than 32 bytes. Set the Jwt__Key environment variable " +
        "(openssl rand -base64 48) before starting the API in production.");
}

// Liveness probe used by the Docker healthcheck and an uptime monitor. It only proves the
// process answers; a database outage is caught by the endpoints that need it.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

if (app.Environment.IsDevelopment())
{
    // JSON document at /openapi/v1.json, Swagger-like UI at /scalar/v1.
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// Must run before authentication/authorization so that even an error response
// (401, 404...) carries the CORS headers the browser requires.
app.UseCors("Frontend");

app.UseAuthentication();  // validates the JWT on every request
app.UseAuthorization();   // checks access rights

app.MapControllers();

app.Run();