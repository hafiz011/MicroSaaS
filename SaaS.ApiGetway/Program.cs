var builder = WebApplication.CreateBuilder(args);

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


app.Use(async (context, next) =>
{
    Console.WriteLine($"Proxying: {context.Request.Method} {context.Request.Path}");
    await next();
});

app.UseStaticFiles();

app.UseCors();
app.MapReverseProxy();

app.MapGet("/", () => "SaaS API Gateway Running...");

app.Run();
