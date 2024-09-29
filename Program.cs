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

var dbConnectionString = await ssmClient.GetParameterAsync(new GetParameterRequest { Name = dbConnectionStringPath });
var dbName = await ssmClient.GetParameterAsync(new GetParameterRequest { Name = dbNamePath });
var sqsUrl = await ssmClient.GetParameterAsync(new GetParameterRequest { Name = sqsUrlPath });

await ConfigureDatabase(builder, dbConnectionString.Parameter.Value, dbName.Parameter.Value);
builder.Services.AddHostedService<MessageProcessingService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.Run();

async Task ConfigureDatabase(WebApplicationBuilder builder, string dbConnectionString, string dbName){
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