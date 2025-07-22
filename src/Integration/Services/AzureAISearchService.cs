using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using Entities;
using System.Text.Json;
using Microsoft.Extensions.Configuration;


namespace Integration.Services
{
    public class AzureAISearchService : IAzureAISearchService
    {
        private readonly string _indexName;
        private readonly SearchClient _searchClient;
        private readonly SearchIndexerClient _indexerClient;
        private readonly ILogger<AzureAISearchService> _logger;

        /// <summary>
        /// Initializes a new instance of the AzureAISearchService class using IConfiguration.
        /// </summary>        
        public AzureAISearchService(IConfiguration configuration, ILogger<AzureAISearchService> logger)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            var searchEndpoint = configuration["AzureAISearch:Endpoint"]
                ?? throw new InvalidOperationException("Missing AzureAISearch:Endpoint in configuration.");

            var secretName = configuration["AzureAISearch:Key1:SecretName"];

            var searchKey = !string.IsNullOrWhiteSpace(secretName)
                ? configuration[secretName] ?? configuration["AzureAISearch:Key1"]
                : configuration["AzureAISearch:Key1"]
                ?? throw new InvalidOperationException("Missing AzureAISearch:Key1 in configuration.");

            _indexName = configuration["AzureAISearch:IndexName"]
                ?? throw new InvalidOperationException("Missing AzureAISearch:IndexName in configuration.");

            try
            {
                _logger = logger ?? throw new ArgumentNullException(nameof(logger));

                // Initialize SearchClient and SearchIndexerClient
                var uri = new Uri(searchEndpoint);
                var credential = new AzureKeyCredential(searchKey!);
                _searchClient = new SearchClient(uri, _indexName, credential);
                _indexerClient = new SearchIndexerClient(uri, credential);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to initialize AzureAISearchService.", ex);
            }
        }

        /// <summary>
        /// Triggers the specified indexer in Azure Search.
        /// </summary>
        public async Task RunIndexerAsync()
        {
            try
            {
                // Trigger the indexer asynchronously
                await _indexerClient.RunIndexerAsync(_indexName);
                _logger.LogInformation("Indexer '{IndexName}' triggered successfully.", _indexName);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError("Azure Search Indexer request failed: {ErrorMessage}", ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError("Unexpected error while triggering the indexer: {ErrorMessage}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Uploads a list of enriched document chunks directly to the Azure Search index.
        /// </summary>
        /// <param name="enrichedChunks">A list of tuples containing the chunk path and JSON content for each document.</param>
        /// <returns>A task that represents the asynchronous operation. Returns true if the documents were uploaded successfully.</returns>
        public async Task<bool> UploadFilesAsync(List<DocumentChunk> enrichedChunks)
        {
            try
            {
                // Prepare the documents to upload
                var documents = new List<SearchDocument>();

                foreach (var chunk in enrichedChunks)
                {
                    string chunkPath = chunk.ChunkPath;
                    string jsonContent = chunk.JsonContent;

                    try
                    {                       

                        // Encode chunkPath to a URL-safe Base64 string for use as a valid ID
                        var encodedId = EncodeToUrlSafeBase64(chunkPath);

                        // Deserialize the JSON content into a dictionary
                        var contentData = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonContent);

                        // Build the SearchDocument with required and custom fields
                        var document = new SearchDocument
                        {
                            ["id"] = encodedId, // Use the encoded chunkPath as the ID
                            ["file_name"] = TryGetValueOrNull(contentData, "file_name"),
                            ["file_uri"] = TryGetValueOrNull(contentData, "file_uri"),
                            ["chunk_uri"] = TryGetValueOrNull(contentData, "chunk_uri"),
                            ["processed_datetime"] = TryGetValueOrNull(contentData, "processed_datetime"),
                            ["chunk_file"] = TryGetValueOrNull(contentData, "chunk_file"),
                            ["file_class"] = TryGetValueOrNull(contentData, "file_class"),
                            ["folder"] = TryGetValueOrNull(contentData, "folder"),
                            ["pages"] = TryGetValueOrNull(contentData, "pages"),
                            ["title"] = TryGetValueOrNull(contentData, "title"),
                            ["subtitle"] = TryGetValueOrNull(contentData, "subtitle"),
                            ["section"] = TryGetValueOrNull(contentData, "section"),
                            ["content"] = TryGetValueOrNull(contentData, "content"),
                            ["context"] = TryGetValueOrNull(contentData, "context"),
                            ["token_count"] = TryGetValueOrNull(contentData, "token_count"),
                            ["key_phrases"] = TryGetValueOrNull(contentData, "keyphrases"),
                            ["entities"] = TryGetValueOrNull(contentData, "entities"),
                            ["contentVector"] = TryGetValueOrNull(contentData, "contentVector")
                        };

                        documents.Add(document);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError("Error parsing JSON for chunk '{ChunkPath}': {ErrorMessage}", chunkPath, ex.Message);                        
                        throw new InvalidOperationException($"Failed to parse JSON content for chunk '{chunkPath}'.", ex);
                    }
                }

                // Upload the documents to the index
                try
                {
                    var response = await _searchClient.UploadDocumentsAsync(documents);

                    // Log the results for each document
                    foreach (var result in response.Value.Results)
                    {
                        _logger.LogInformation("Document with ID '{DocumentId}' uploaded successfully: {Succeeded}", result.Key, result.Succeeded);
                    }

                    return true;
                }
                catch (RequestFailedException ex)
                {
                    _logger.LogError("Azure Search upload request failed: {ErrorMessage}", ex.Message);
                    throw;
                }
            }
            catch (Exception ex)
            {
                // Log and rethrow any errors encountered during upload
                _logger.LogError("Error uploading documents: {ErrorMessage}", ex.Message);
                throw;
            }
        }

        // Helper method to safely get values from the dictionary
        private static object? TryGetValueOrNull(Dictionary<string, object>? dictionary, string key)
        {
            if (dictionary != null && dictionary.TryGetValue(key, out var value))
            {
                return value;
            }
            return null;
        }

        /// <summary>
        /// Encodes a string to a URL-safe Base64 format.
        /// </summary>
        /// <param name="input">The input string to encode.</param>
        /// <returns>The URL-safe Base64 encoded string.</returns>
        private static string EncodeToUrlSafeBase64(string input)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(input);
            return Convert.ToBase64String(bytes)
                          .TrimEnd('=') // Remove padding characters
                          .Replace('+', '-') // Replace '+' with '-'
                          .Replace('/', '_'); // Replace '/' with '_'
        }
    }
}
