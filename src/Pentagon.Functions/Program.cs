using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pentagon.Functions.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights()
    .AddSingleton<GoogleCredentialProvider>()
    .AddSingleton<IImageAnalyzerService, ImageAnalyzerService>()
    .AddSingleton<IInterpreterService, InterpreterService>()
    .AddSingleton<IMessageService, MessageService>();

builder.Build().Run();
