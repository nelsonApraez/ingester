using Azure.Identity;
using Entities;
using FunctionIngester;
using FunctionIngester.Helpers;
using FunctionIngester.Helpers.Interfaces;
using FunctionIngester.Interfaces;
using FunctionIngester.Utils;
using FunctionIngester.Utils.Interfaces;
using Integration.Interfaces;
using Integration.Services;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureAppConfiguration((context, config) =>
    {
        var builtConfig = config.Build();

        config
            // Load local.settings.json for local development
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
            // Load environment variables (takes priority over local.settings.json)
            .AddEnvironmentVariables();

        // Retrieve the Azure Key Vault endpoint from environment variables or configuration
        var keyVaultUri = builtConfig["KeyVault:VaultUri"] ?? Environment.GetEnvironmentVariable("KEYVAULT_URI");

        if (!string.IsNullOrEmpty(keyVaultUri))
        {
            // Use DefaultAzureCredential to authenticate automatically with Managed Identity or local credentials
            var credential = new DefaultAzureCredential();
            config.AddAzureKeyVault(new Uri(keyVaultUri), credential);
        }
    })
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;

        // Register IConfiguration for dependency injection
        services.AddSingleton<IConfiguration>(configuration);

        // Register StorageFolders as a singleton
        services.AddSingleton(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            return new StorageFolders
            {
                ChunkedDocs = config["AzureStorage:Folders:ChunkedDocs"] ?? "",
                FailedProcessingDocs = config["AzureStorage:Folders:FailedProcessingDocs"] ?? "",
                UnprocessedDocs = config["AzureStorage:Folders:UnprocessedDocs"] ?? "",
                ProcessedDocs = config["AzureStorage:Folders:ProcessedDocs"] ?? ""
            };
        });


        // Register application services using IConfiguration directly in constructors
        services
            .AddLogging()
            .AddSingleton<IBlobStorageService, BlobStorageService>()
            .AddSingleton<IAzureAISearchService, AzureAISearchService>()
            .AddSingleton<IAzureAIService, AzureAIService>()
            .AddSingleton<IOpenAIService, OpenAIService>()
            .AddSingleton<IDocumentStorage, DocumentStorage>()
            .AddSingleton<IDocumentAnalyzer, DocumentAnalyzer>()
            .AddSingleton<IDocumentChunker, DocumentChunker>()
            .AddSingleton<IDocumentEnricher, DocumentEnricher>()
            .AddSingleton<IProcessorUtil, ProcessorUtil>()
            .AddSingleton<IAnalyzerUtil, AnalyzerUtil>()
            .AddSingleton<IDocumentProcessor, DocumentProcessor>()
            .AddSingleton<FunctionDocumentProcessor>();
    })
    .ConfigureOpenApi()
    .Build();

// Run the Azure Function host
await host.RunAsync();
