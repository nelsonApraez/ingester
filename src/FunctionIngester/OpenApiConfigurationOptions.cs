using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;

namespace FunctionIngester
{
    public class OpenApiConfigurationOptions : IOpenApiConfigurationOptions
    {
        public OpenApiInfo Info { get; set; } =
          new OpenApiInfo
          {
              Title = "Document Processing API",
              Version = "1.0",
              Description = "This API processes documents from Azure Blob Storage by analyzing, chunking, enriching, and uploading them to Azure Storage and a vector database.",
              Contact = new OpenApiContact
              {
                  Name = "API Support",
                  Email = "support@example.com"
              }
          };


        public List<OpenApiServer> Servers { get; set; } = new();

        public OpenApiVersionType OpenApiVersion { get; set; } = OpenApiVersionType.V3;

        public bool IncludeRequestingHostName { get; set; }
        public bool ForceHttp { get; set; }
        public bool ForceHttps { get; set; } = true;
        public List<IDocumentFilter> DocumentFilters { get; set; } = new();
    }
}
