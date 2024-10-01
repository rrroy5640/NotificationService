using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Amazon.SQS;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using NotificationService.Models;
using NotificationService.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var awsParameterStore = builder.Configuration.GetSection("AWS:ParameterStore");

builder.Services.AddAWSService<IAmazonSimpleSystemsManagement>();
builder.Services.AddAWSService<IAmazonSQS>();

var awsOptions = builder.Configuration.GetAWSOptions();
var ssmClient = awsOptions.CreateServiceClient<IAmazonSimpleSystemsManagement>();
var sqsClient = awsOptions.CreateServiceClient<IAmazonSQS>();

var dbConnectionStringPath = awsParameterStore["DbConnectionStringPath"];
var dbNamePath = awsParameterStore["DbNamePath"];
var sqsUrlPath = awsParameterStore["SQSUrlPath"];

if (string.IsNullOrEmpty(dbConnectionStringPath) || string.IsNullOrEmpty(dbNamePath) || string.IsNullOrEmpty(sqsUrlPath))
{
    throw new Exception("Missing configuration");
}

try
{
    var dbConnectionString = (await ssmClient.GetParameterAsync(new GetParameterRequest { Name = dbConnectionStringPath, WithDecryption = true })).Parameter.Value;
    var dbName = (await ssmClient.GetParameterAsync(new GetParameterRequest { Name = dbNamePath, WithDecryption = true })).Parameter.Value;
    var sqsUrl = (await ssmClient.GetParameterAsync(new GetParameterRequest { Name = sqsUrlPath })).Parameter.Value;
    Console.WriteLine($"DB Connection String: {dbConnectionString}");
    Console.WriteLine($"DB Name: {dbName}");
    Console.WriteLine($"SQS URL: {sqsUrl}");

    ConfigureDatabase(builder, dbConnectionString, dbName);
    ConfigureSQS(builder, sqsUrl);
}
catch (Exception ex)
{
    throw new Exception("Error retrieving parameters from AWS Parameter Store", ex);
}

builder.Services.AddHostedService<MessageProcessingService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.Run();

void ConfigureDatabase(WebApplicationBuilder builder, string dbConnectionString, string dbName)
{
    builder.Services.AddSingleton<IMongoDBSettings>(sp =>
    {
        var settings = new MongoDBSettings
        {
            ConnectionString = dbConnectionString,
            DatabaseName = dbName
        };
        return settings;
    });

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

void ConfigureSQS(WebApplicationBuilder builder, string sqsUrl)
{
    builder.Services.Configure<SQSSettings>(settings =>
    {
        settings.QueueUrl = sqsUrl;
    });

    builder.Services.AddSingleton<ISQSSettings>(sp =>
        sp.GetRequiredService<IOptions<SQSSettings>>().Value
    );

    builder.Services.AddSingleton<IAmazonSQS>(sp =>
    {
        var options = new AmazonSQSConfig { ServiceURL = sqsUrl };
        return new AmazonSQSClient(options);
    });
}