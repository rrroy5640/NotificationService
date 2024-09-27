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

var dbConnectionStringPath = awsParameterStore["DbConnectionStringPath"];
var dbNamePath = awsParameterStore["DbNamePath"];
var sqsUrlPath = awsParameterStore["SQSUrlPath"];

if (string.IsNullOrEmpty(dbConnectionStringPath) || string.IsNullOrEmpty(dbNamePath) || string.IsNullOrEmpty(sqsUrlPath))
{
    throw new Exception("Missing configuration");
}

await ConfigureDatabase(builder);
await ConfigureSQS(builder);

builder.Services.AddHostedService<MessageProcessingService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.Run();

async Task ConfigureDatabase(WebApplicationBuilder builder)
{
    var ssmClient = builder.Services.BuildServiceProvider().GetRequiredService<IAmazonSimpleSystemsManagement>();
    var connectionStringResponse = await ssmClient.GetParameterAsync(new GetParameterRequest { Name = dbConnectionStringPath, WithDecryption = true });
    var dbNameResponse = await ssmClient.GetParameterAsync(new GetParameterRequest { Name = dbNamePath, WithDecryption = true });

    builder.Services.Configure<MongoDBSettings>(options =>
    {
        options.ConnectionString = connectionStringResponse.Parameter.Value;
        options.DatabaseName = dbNameResponse.Parameter.Value;
    });

    builder.Services.AddSingleton<IMongoClient>(sp =>
    {
        var settings = sp.GetRequiredService<IOptions<MongoDBSettings>>().Value;
        return new MongoClient(settings.ConnectionString);
    });
}

async Task ConfigureSQS(WebApplicationBuilder builder)
{
    var ssmClient = builder.Services.BuildServiceProvider().GetRequiredService<IAmazonSimpleSystemsManagement>();
    var sqsUrlResponse = await ssmClient.GetParameterAsync(new GetParameterRequest { Name = sqsUrlPath, WithDecryption = true });

    builder.Services.Configure<SQSSettings>(options =>
    {
        options.QueueUrl = sqsUrlResponse.Parameter.Value;
    });
}