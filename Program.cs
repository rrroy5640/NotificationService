using System.Text;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using NotificationService.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

var jwtSettings = app.Configuration.GetSection("JwtSettings");
var awsParameterStore = app.Configuration.GetSection("AWS:ParameterStore");

builder.Services.AddAWSService<IAmazonSimpleSystemsManagement>();
var awsOptions = builder.Configuration.GetAWSOptions();
var ssmClient = awsOptions.CreateServiceClient<IAmazonSimpleSystemsManagement>();

var jwtSecretKeyPath = awsParameterStore["JwtSecretKeyPath"];
var dbConnectionStringPath = awsParameterStore["DbConnectionStringPath"];
var dbNamePath = awsParameterStore["DbNamePath"];

var issuer = jwtSettings["Issuer"];
var audience = jwtSettings["Audience"];

var jwtSecretKey = await ssmClient.GetParameterAsync(new GetParameterRequest { Name = jwtSecretKeyPath });
var dbConnectionString = await ssmClient.GetParameterAsync(new GetParameterRequest { Name = dbConnectionStringPath });
var dbName = await ssmClient.GetParameterAsync(new GetParameterRequest { Name = dbNamePath });

if(string.IsNullOrEmpty(issuer) || string.IsNullOrEmpty(audience) || string.IsNullOrEmpty(jwtSecretKeyPath) || string.IsNullOrEmpty(dbConnectionStringPath) || string.IsNullOrEmpty(dbNamePath))
{
    throw new Exception("Missing configuration");
}

await ConfigureJwtAuthentication(builder, ssmClient, jwtSecretKeyPath, issuer, audience);
await ConfigureDatabase(builder, ssmClient, dbConnectionStringPath, dbNamePath);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.Run();

async Task ConfigureJwtAuthentication(WebApplicationBuilder builder, IAmazonSimpleSystemsManagement ssmClient, string jwtSecretKeyPath, string jwtIssuer, string jwtAudience)
{
    try
    {
        var parameterResponse = await ssmClient.GetParameterAsync(new GetParameterRequest
        {
            Name = jwtSecretKeyPath,
            WithDecryption = true
        });

        var secretKey = parameterResponse.Parameter.Value;
        builder.Services.AddAuthentication(option =>
            {
                option.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                option.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            }).AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtIssuer,
                    ValidAudience = jwtAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
                };
            });
    }
    catch (Exception e)
    {
        Console.WriteLine($"Error retrieving JWT secret key: {e.Message}");
        throw;
    }

}

async Task ConfigureDatabase(WebApplicationBuilder builder, IAmazonSimpleSystemsManagement ssmClient, string dbConnectionStringPath, string dbNamePath)
{
    try
    {
        var connectionStringParameterResponse = await ssmClient.GetParameterAsync(new GetParameterRequest
        {
            Name = dbConnectionStringPath,
            WithDecryption = true
        });

        var databaseStringParameterResponse = await ssmClient.GetParameterAsync(new GetParameterRequest
        {
            Name = dbNamePath,
            WithDecryption = true
        });

        var connectionString = connectionStringParameterResponse.Parameter.Value;
        var databaseName = databaseStringParameterResponse.Parameter.Value;
        builder.Services.Configure<MongoDBSettings>(option =>
        {
            option.ConnectionString = connectionString;
            option.DatabaseName = databaseName;
        });

        builder.Services.AddSingleton<IMongoDBSettings>(sp =>
         sp.GetRequiredService<IOptions<MongoDBSettings>>().Value
        );

        builder.Services.AddSingleton<IMongoClient>(sp =>
        {
            var settings = sp.GetRequiredService<IMongoDBSettings>();
            return new MongoClient(settings.ConnectionString);
        });

        builder.Services.AddScoped(sp =>
        {
            var client = sp.GetRequiredService<IMongoClient>();
            var settings = sp.GetRequiredService<IMongoDBSettings>();
            return client.GetDatabase(settings.DatabaseName);
        });
    }
    catch (Exception e)
    {
        Console.WriteLine($"Error retrieving database connection string: {e.Message}");
        throw;
    }
}