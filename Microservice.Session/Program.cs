using Microservice.Session.Infrastructure.AlertNotifier;
using Microservice.Session.Infrastructure.GeoIPService;
using Microservice.Session.Infrastructure.Interfaces;
using Microservice.Session.Infrastructure.MongoDb;
using Microservice.Session.Infrastructure.Repositories;
using Microservice.Session.Infrastructure.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using RabbitMQ.Client;

namespace Microservice.Session
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.Configure<MongoDbSettings>(builder.Configuration.GetSection("MongoDbSettings"));
            builder.Services.AddSingleton<MongoDbContext>();



            // Add services to the container.
            builder.Services.AddScoped<EmailService>();
            builder.Services.AddHostedService<SessionTimeOutService>();
            builder.Services.AddHostedService<SuspiciousSessionDetection>();
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
                var factory = new ConnectionFactory() { HostName = "localhost" };
                var connection = factory.CreateConnection();
                return connection.CreateModel();
            });


            builder.Services.AddControllers();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();

            
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
