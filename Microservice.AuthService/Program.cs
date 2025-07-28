
using Microservice.AuthService.Database;
using Microservice.AuthService.Entities;
using Microservice.AuthService.Infrastructure.Interfaces;
using Microservice.AuthService.Infrastructure.Repositories;
using Microservice.AuthService.Infrastructure.Services;
using Microservice.AuthService.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using RabbitMQ.Client;
using System.Text;

namespace Microservice.AuthService
{
    public class Program
    {
       // public static void Main(string[] args)
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.Configure<MongoDbSettings>(builder.Configuration.GetSection("MongoDbSettings"));
            builder.Services.AddSingleton<MongoDbContext>();

            // Add services to the container.
            builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(identityOptions =>
            {
                identityOptions.Password.RequireDigit = true;
                identityOptions.Password.RequiredLength = 6;
                identityOptions.Password.RequireNonAlphanumeric = false;
                identityOptions.Password.RequireUppercase = true;
                identityOptions.Password.RequireLowercase = true;
                identityOptions.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                identityOptions.Lockout.MaxFailedAccessAttempts = 5;
                identityOptions.Lockout.AllowedForNewUsers = true;
                identityOptions.User.RequireUniqueEmail = true;
            })
            .AddMongoDbStores<ApplicationUser, ApplicationRole, Guid>(
                builder.Configuration["MongoDbSettings:ConnectionString"],
                builder.Configuration["MongoDbSettings:DatabaseName"])
            .AddDefaultTokenProviders();

            // Add Authentication using JWT
            builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                var jwtSettings = builder.Configuration.GetSection("JwtSettings");
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSettings["Issuer"],
                    ValidAudience = jwtSettings["Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Key"]))
                };
            });

            builder.Services.AddScoped<EmailService>();

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", policy =>
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyHeader()
                          .AllowAnyMethod();
                });
            });

            // RabbitMQ connection factory
            builder.Services.AddSingleton<IConnectionFactory>(sp =>
            {
                return new ConnectionFactory()
                {
                    HostName = builder.Configuration["RabbitMq:HostName"], // e.g., "localhost"
                    UserName = builder.Configuration["RabbitMq:UserName"],
                    Password = builder.Configuration["RabbitMq:Password"],
                    DispatchConsumersAsync = true // recommended for async consumer
                };
            });

            // RabbitMQ connection
            builder.Services.AddSingleton<IConnection>(sp =>
            {
                var factory = sp.GetRequiredService<IConnectionFactory>();
                return factory.CreateConnection();
            });

            // RabbitMQ channel (IModel)
            builder.Services.AddSingleton<IModel>(sp =>
            {
                var connection = sp.GetRequiredService<IConnection>();
                return connection.CreateModel();
            });



            builder.Services.AddSingleton<GrpcServiceClient>();

            builder.Services.AddSingleton<ISuspiciousActivityRepository, SuspiciousActivityRepository>();
            builder.Services.AddHostedService<RabbitMqConsumerService>();




            // Seed indexes
            builder.Services.AddTransient<IndexSeeder>();


            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();



            // Seed indexes at startup
            using (var scope = app.Services.CreateScope())
            {
                var seeder = scope.ServiceProvider.GetRequiredService<IndexSeeder>();
                await seeder.SeedIndexesAsync();
            }


            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();


            var wwwrootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            if (Directory.Exists(wwwrootPath) && Path.IsPathRooted(wwwrootPath))
            {
                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(wwwrootPath),
                    RequestPath = ""
                });
            }

            //var wwwrootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            //if (Directory.Exists(wwwrootPath))
            //{
            //    app.UseStaticFiles(new StaticFileOptions
            //    {
            //        FileProvider = new PhysicalFileProvider(wwwrootPath),
            //        RequestPath = ""
            //    });
            //}




            app.UseCors("AllowAll");

            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();

            app.Run();
        }
    }
}