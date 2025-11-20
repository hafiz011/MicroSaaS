using Microservice.Session.Entities;
using Microservice.Session.Infrastructure.GeoIPService;
using Microservice.Session.Infrastructure.Interfaces;
using Microservice.Session.Infrastructure.MongoDb;
using Microservice.Session.Infrastructure.Repositories;
using Microservice.Session.Infrastructure.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using RabbitMQ.Client;

namespace Microservice.Session
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders =
                    ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
            });



            builder.Services.Configure<MongoDbSettings>(builder.Configuration.GetSection("MongoDbSettings"));
            builder.Services.AddSingleton<MongoDbContext>();



            // Add services to the container.
            builder.Services.AddHostedService<SessionTimeOutService>();
            builder.Services.AddHttpClient<GeolocationService>();
            builder.Services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
            builder.Services.AddScoped<IActivityRepository, ActivityRepository>();
            builder.Services.AddScoped<ISessionRepository, SessionRepository>();
            builder.Services.AddScoped<IUserInfoRepository, UserInfoRepository>();
           
            builder.Services.AddSingleton<IRabbitMqPublisher, RabbitMqPublisher>();



            builder.Services.AddGrpc();

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenAnyIP(5002, o => o.Protocols = HttpProtocols.Http1);   // REST
                options.ListenAnyIP(7002, o => o.Protocols = HttpProtocols.Http2);   // gRPC   
            });

            builder.Services.AddSingleton<IModel>(sp =>
            {
                var factory = new ConnectionFactory() { HostName = "rabbitmq" };
                var connection = factory.CreateConnection();
                return connection.CreateModel();
            });

            // Seed indexes
            builder.Services.AddTransient<IndexSeeder>();

            // GeoIP service using MaxMind GeoLite2 database
            builder.Services.AddSingleton<IGeoLocationServiceGeoLite2>(sp =>
                new GeoLocationServiceGeoLite2(
                    "/app/GeoLite2-City.mmdb",
                    sp.GetRequiredService<ILogger<GeoLocationServiceGeoLite2>>()
                )
            );


            builder.Services.AddControllers();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();

            app.UseForwardedHeaders();

            app.UseCors(cors =>
                cors.SetIsOriginAllowed(_ => true)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials()
            );



            // Seed indexes at startup
            using (var scope = app.Services.CreateScope())
            {
                var seeder = scope.ServiceProvider.GetRequiredService<IndexSeeder>();
                await seeder.SeedIndexesAsync();
            }



            app.MapGrpcService<GrpcServer>();



            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
