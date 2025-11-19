using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;

    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});


builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();
app.UseForwardedHeaders();

// (Then any other middleware)
app.Use(async (context, next) =>
{
    var ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault()
           ?? context.Connection.RemoteIpAddress?.ToString();
    await next();
});

//app.Use(async (context, next) =>
//{
//    Console.WriteLine($"Proxying: {context.Request.Method} {context.Request.Path}");
//    await next();
//});

app.UseStaticFiles();
app.UseCors();
app.MapReverseProxy();

app.MapGet("/", () => "SaaS API Gateway Running...");

app.Run();
