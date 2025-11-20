using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Forwarded headers
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;

    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Reverse Proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// FIXED CORS POLICY
builder.Services.AddCors(options =>
{
    options.AddPolicy("TracklyPolicy", policy =>
    {
        policy
            .SetIsOriginAllowed(_ => true)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

app.UseForwardedHeaders();

// Use the single correct CORS here
app.UseCors("TracklyPolicy");

// Middleware to log IP
app.Use(async (context, next) =>
{
    var ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault()
           ?? context.Connection.RemoteIpAddress?.ToString();
    await next();
});

app.UseStaticFiles();

// Reverse Proxy Mapping
app.MapReverseProxy();

app.MapGet("/", () => "SaaS API Gateway Running...");

app.Run();
