using System.Text.Encodings.Web;
using System.Text.Json;
using Azure.Storage.Blobs;
using Entities;
using FunctionIngester.Utils.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FunctionIngester.Utils
{
    public class ProcessorUtil : IProcessorUtil
    {
        private readonly BlobContainerClient _blobContainerClient;
        private readonly ILogger<ProcessorUtil> _logger;
        private readonly StorageFolders _storageFolders;

        public ProcessorUtil(IConfiguration configuration, StorageFolders storageFolder, ILogger<ProcessorUtil> logger)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            // Attempt to get the secret name for the connection string, if defined.
            var secretName = configuration["AzureStorage:ConnectionString:SecretName"];

            // Retrieve the connection string using the secret if available;
            // otherwise, fall back to the directly configured connection string.
            var connectionString = !string.IsNullOrWhiteSpace(secretName)
                ? configuration[secretName] ?? configuration["AzureStorage:ConnectionString"]
                : configuration["AzureStorage:ConnectionString"]
                ?? throw new InvalidOperationException("Missing Azure Storage Connection String in configuration");

            // Retrieve the container name from the configuration.
            var azureStorageContainer = configuration["AzureStorage:Container"]
                ?? throw new InvalidOperationException("Missing Azure Storage Container in configuration");

            // Create the BlobContainerClient using the obtained connection string and container name.
            _blobContainerClient = new BlobServiceClient(connectionString).GetBlobContainerClient(azureStorageContainer);

            _storageFolders = storageFolder ?? throw new ArgumentNullException(nameof(storageFolder));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public List<DocumentChunk> AddAdditionalData(List<DocumentChunk> chunks, string myBlobName)
        {
            _logger.LogInformation($"Adding additional data to chunked documents...");

            // Validate input enrichedChunks list
            if (chunks == null || chunks.Count == 0)
            {
                _logger.LogError("The chunks list is null or empty. Cannot proceed with enrichment.");
                throw new ArgumentException("The chunks list cannot be null or empty.", nameof(chunks));
            }

            var chunksWithAdditonalData = new List<DocumentChunk>();

            try
            {
                int fileNumber = 0;

                foreach (var chunk in chunks)
                {

                    string chunkPath = chunk.ChunkPath;
                    string jsonContent = chunk.JsonContent;

                    // Validate chunkPath and jsonContent
                    if (string.IsNullOrEmpty(chunkPath))
                    {
                        _logger.LogError("Chunk path is null or empty.");
                        throw new ArgumentException("Each chunk must have a valid chunkPath.", nameof(chunks));
                    }

                    if (string.IsNullOrEmpty(jsonContent))
                    {
                        _logger.LogError($"Chunk with path {chunkPath} has null or empty content.");
                        throw new ArgumentException($"Chunk with path {chunkPath} must have valid JSON content.", nameof(chunks));
                    }

                    // Deserialize the existing chunk JSON
                    Dictionary<string, object> chunkData;
                    try
                    {
                        chunkData = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonContent)!;
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError($"Failed to deserialize JSON content for chunk {chunkPath}: {ex.Message}");
                        throw new ArgumentException($"Invalid JSON content for chunk {chunkPath}.", ex);
                    }

                    if (chunkData == null)
                    {
                        _logger.LogError($"Chunk data deserialized from {chunkPath} is null.");
                        throw new ArgumentException($"Chunk data from {chunkPath} cannot be null.", nameof(chunks));
                    }

                    // Validate and retrieve the chunk_file property from chunkData
                    if (!chunkData.TryGetValue("chunk_file", out var chunkFileObj) || chunkFileObj == null || string.IsNullOrEmpty(chunkFileObj.ToString()))
                    {
                        _logger.LogError($"Chunk with path {chunkPath} does not contain a valid chunk_file.");
                        throw new ArgumentException($"Chunk with path {chunkPath} must contain a 'chunk_file' property with a valid value.", nameof(chunks));
                    }

                    // Validate content property in the chunk data
                    if (!chunkData.TryGetValue("content", out var contentObj) || contentObj == null || string.IsNullOrEmpty(contentObj.ToString()))
                    {
                        _logger.LogError($"Chunk with path {chunkPath} does not contain valid content.");
                        throw new ArgumentException($"Chunk with path {chunkPath} must contain a 'content' property with valid text.", nameof(chunks));
                    }                    

                    string chunkFile = chunkFileObj.ToString()!; 

                    // Add new data properties to the existing chunk data
                    chunkData["chunk_uri"] = $"{ContainerBaseUri}/{_storageFolders.ChunkedDocs}/{myBlobName}-{fileNumber}.json";

                    _logger.LogInformation($"Adding additional data to chunk {chunkFile}");

                    // Serialize the enriched chunk back to JSON
                    string chunksJson = JsonSerializer.Serialize(chunkData, SerializerOptions);

                    // Add the enriched chunk to the result list
                    chunksWithAdditonalData.Add(new DocumentChunk
                    {
                        ChunkPath = chunkPath,
                        JsonContent = chunksJson
                    });

                    fileNumber++;
                }

                return chunksWithAdditonalData;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error adding additional data to document chunks: {ex.Message}.");
                throw;
            }
        }

        /// <summary>
        /// Gets the base URI of the Azure Blob Storage container.
        /// </summary>
        /// <param name="_blobContainerClient">The BlobContainerClient instance for the container.</param>
        /// <returns>The base URI of the container.</returns>
        /// <summary>
        /// Gets the base URI of the Azure Blob Storage container.
        /// </summary>
        public string ContainerBaseUri
        {
            get
            {
                try
                {
                    // Get the BlobContainerClient URI
                    Uri containerUri = _blobContainerClient.Uri;

                    // Return the URI as a string
                    return containerUri.ToString();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error retrieving container base URI: {ex.Message}");
                    throw;
                }
            }
        }

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };


    }
}
