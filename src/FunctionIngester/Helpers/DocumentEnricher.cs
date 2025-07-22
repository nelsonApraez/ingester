using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Integration.Interfaces;
using Integration.Services;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using Entities;
using FunctionIngester.Helpers.Interfaces;

namespace FunctionIngester.Helpers
{
    public class DocumentEnricher : IDocumentEnricher
    {
        private readonly ILogger<DocumentEnricher> _logger;
        private readonly IAzureAIService _azureAIService;
        private readonly IOpenAIService _openAIService;

        public DocumentEnricher(IAzureAIService azureAIService, IOpenAIService openAIService, ILogger<DocumentEnricher> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _azureAIService = azureAIService ?? throw new ArgumentNullException(nameof(azureAIService));
            _openAIService = openAIService ?? throw new ArgumentNullException(nameof(openAIService));
        }

        /// <summary>
        /// Enriches each chunk of the document with additional metadata (key phrases, entities, etc.).
        /// </summary>
        /// <param name="chunksWithAdditionalData">List of (chunkPath, jsonContent) to be enriched.</param>
        /// <returns>List of enriched (chunkPath, jsonContent).</returns>
        public async Task<List<DocumentChunk>> EnrichDocumentAsync(
            List<DocumentChunk> chunksWithAdditionalData)
        {
            _logger.LogInformation("Enriching Document...");

            // 1) Validate the overall input
            ValidateInputChunks(chunksWithAdditionalData);

            var enrichedChunks = new List<DocumentChunk>();
            int fileNumber = 0;

            try
            {
                // 2) Process each chunk
                foreach (var chunk in chunksWithAdditionalData)
                {

                    string chunkPath = chunk.ChunkPath;
                    string jsonContent = chunk.JsonContent;

                    // Process and enrich the single chunk
                    var enrichedChunk = await ProcessChunk(chunkPath, jsonContent, fileNumber);
                    enrichedChunks.Add(enrichedChunk);
                    fileNumber++;
                }

                return enrichedChunks;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error enriching document: {ex.Message}.");
                throw;
            }
        }

        /// <summary>
        /// Processes and enriches a single chunk.
        /// </summary>
        private async Task<DocumentChunk> ProcessChunk(
            string chunkPath,
            string jsonContent,
            int fileNumber)
        {
            // Validate chunk path and content
            ValidateChunkPathAndContent(chunkPath, jsonContent);

            // Deserialize the existing chunk JSON
            var chunkData = DeserializeChunkJson(jsonContent, chunkPath);

            // Validate chunk data
            ValidateChunkData(chunkData, chunkPath);

            // Retrieve necessary fields
            string chunkFile = chunkData["chunk_file"].ToString()!;
            string chunkText = chunkData["content"].ToString()!;

            // Clean up the chunk text
            _logger.LogInformation($"Cleaning chunk {chunkFile} content before enrichment...");
            chunkText = CleanTextWithRegex(chunkText);

            // Enrich chunk content in parallel
            _logger.LogInformation($"Enriching chunk {chunkFile} and generating embeddings...");
            var (keyphrases, entities, context, contentVector) = await EnrichChunkTextAsync(chunkText);

            // Add new enrichment properties to the existing chunk data
            AddEnrichmentProperties(chunkData, keyphrases, entities, context, contentVector);

            // Serialize the enriched chunk back to JSON
            string enrichedJson = SerializeChunkData(chunkData);

            return new DocumentChunk { ChunkPath = chunkPath, JsonContent = enrichedJson };
        }

        /// <summary>
        /// Validates that the overall list of chunks is not null or empty.
        /// </summary>
        private void ValidateInputChunks(List<DocumentChunk> chunks)
        {
            if (chunks == null || chunks.Count == 0)
            {
                _logger.LogError("The chunks list is null or empty. Cannot proceed with enrichment.");
                throw new ArgumentException("The chunks list cannot be null or empty.", nameof(chunks));
            }
        }

        /// <summary>
        /// Validates that the individual chunk path and JSON content are non-empty.
        /// </summary>
        private void ValidateChunkPathAndContent(string chunkPath, string jsonContent)
        {
            if (string.IsNullOrEmpty(chunkPath))
            {
                _logger.LogError("Chunk path is null or empty.");
                throw new ArgumentException("Each chunk must have a valid chunkPath.");
            }

            if (string.IsNullOrEmpty(jsonContent))
            {
                _logger.LogError($"Chunk with path {chunkPath} has null or empty content.");
                throw new ArgumentException($"Chunk with path {chunkPath} must have valid JSON content.");
            }
        }

        /// <summary>
        /// Deserializes the JSON content into a Dictionary and ensures it is not null.
        /// </summary>
        private Dictionary<string, object> DeserializeChunkJson(string jsonContent, string chunkPath)
        {
            try
            {
                var chunkData = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonContent);
                if (chunkData == null)
                {
                    _logger.LogError($"Chunk data deserialized from {chunkPath} is null.");
                    throw new ArgumentException($"Chunk data from {chunkPath} cannot be null.");
                }
                return chunkData;
            }
            catch (JsonException ex)
            {
                _logger.LogError($"Failed to deserialize JSON content for chunk {chunkPath}: {ex.Message}");
                throw new ArgumentException($"Invalid JSON content for chunk {chunkPath}.", ex);
            }
        }

        /// <summary>
        /// Ensures the chunk data contains 'chunk_file' and 'content' fields.
        /// </summary>
        private void ValidateChunkData(IDictionary<string, object> chunkData, string chunkPath)
        {
            if (!chunkData.TryGetValue("chunk_file", out var chunkFileObj)
                || chunkFileObj == null
                || string.IsNullOrEmpty(chunkFileObj.ToString()))
            {
                _logger.LogError($"Chunk with path {chunkPath} does not contain a valid chunk_file.");
                throw new ArgumentException(
                    $"Chunk with path {chunkPath} must contain a 'chunk_file' property with a valid value.");
            }

            if (!chunkData.TryGetValue("content", out var contentObj)
                || contentObj == null
                || string.IsNullOrEmpty(contentObj.ToString()))
            {
                _logger.LogError($"Chunk with path {chunkPath} does not contain valid content.");
                throw new ArgumentException(
                    $"Chunk with path {chunkPath} must contain a 'content' property with valid text.");
            }
        }

        /// <summary>
        /// Performs parallel calls to AI services to enrich the given chunk text.
        /// </summary>
        private async Task<(List<string> keyphrases, List<string> entities, string context, IList<float> contentVector)>
    EnrichChunkTextAsync(string chunkText)
        {
            var keyphrasesTask = _azureAIService.ExtractKeyPhrasesAsync(chunkText);
            var entitiesTask = _azureAIService.ExtractEntitiesAsync(chunkText);
            var contextTask = ExtractOpenAIContextAsync(chunkText);
            var contentVectorTask = ExtractOpenAIContentVectorAsync(chunkText);

            await Task.WhenAll(keyphrasesTask, entitiesTask, contextTask, contentVectorTask).ConfigureAwait(false);

            var keyphrases = await SafeExecuteAsync(keyphrasesTask, "ExtractKeyPhrasesAsync", new List<string>());
            var entities = await SafeExecuteAsync(entitiesTask, "ExtractEntitiesAsync", new List<string>());
            var context = await SafeExecuteAsync(contextTask, "ExtractOpenAIContextAsync", string.Empty);
            var contentVector = await SafeExecuteAsync(contentVectorTask, "ExtractOpenAIContentVectorAsync", new List<float>());

            return (keyphrases, entities, context ?? string.Empty, contentVector);            
        }

        /// <summary>
        /// Adds the enrichment properties (keyphrases, entities, context, contentVector) to the chunk data.
        /// </summary>
        private static void AddEnrichmentProperties(
            IDictionary<string, object> chunkData,
            List<string> keyphrases,
            List<string> entities,
            string context,
            IList<float> contentVector)
        {
            chunkData["keyphrases"] = keyphrases;
            chunkData["entities"] = entities;
            if (!string.IsNullOrEmpty(context))
            {
                chunkData["context"] = context;
            }
            chunkData["contentVector"] = contentVector;
        }

        /// <summary>
        /// Serializes the chunk data back to JSON using the desired options.
        /// </summary>
        private static string SerializeChunkData(Dictionary<string, object> chunkData)
        {
            return JsonSerializer.Serialize(chunkData, SerializerOptions);
        }

        /// <summary>
        /// Extracts context from the provided content using OpenAI's chat completion service.
        /// </summary>
        /// <param name="content">The input content to process and extract context from.</param>
        /// <returns>A task with the context string generated by the OpenAI model.</returns>
        public async Task<string?> ExtractOpenAIContextAsync(string content)
        {
            try
            {
                var response = await _openAIService.CreateChatCompletionTextAsync(
                    [
                        SystemChatMessage.CreateSystemMessage(Promps.STemplateSystemPerson),
                        SystemChatMessage.CreateAssistantMessage(Promps.STemplateAssitant),
                        SystemChatMessage.CreateUserMessage(content)
                    ]
                );
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"OpenAI context extraction failed: {ex.Message}");
                return null;
            }
        }


        /// <summary>
        /// Generates an embedding vector for the provided content using OpenAI's embedding service.
        /// </summary>
        /// <param name="content">The input content to generate the embedding vector for.</param>
        /// <returns>A task with a list of floats representing the embedding vector.</returns>
        public async Task<IList<float>> ExtractOpenAIContentVectorAsync(string content)
        {
            var response = await _openAIService.GetEmbeddingAsync(content);
            return response;
        }

        /// <summary>
        /// Cleans the provided text by:
        /// 1. Removing HTML tags.
        /// 2. Removing special characters while keeping letters, digits, whitespace, periods, underscores, and dashes.
        /// 3. Converting the text to lowercase.
        /// 4. Replacing multiple whitespaces with a single space and trimming the text.
        /// </summary>
        /// <param name="content">The text content to clean.</param>
        /// <returns>The cleaned text.</returns>
        public static string CleanTextWithRegex(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            { 
                return string.Empty; 
            }

            // 1. Remove HTML tags
            string cleaned = Regex.Replace(content, "<[^>]+>", " ");

            // 2. Remove special characters (keep letters, digits, whitespace, ., _, -)
            cleaned = Regex.Replace(cleaned, @"[^\w\s\.\-_]", " ");

            // 3. Convert to lowercase
            cleaned = cleaned.ToLowerInvariant();

            // 4. Replace multiple whitespaces with a single space and trim
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

            return cleaned;
        }

        /// <summary>
        /// Cleans text using an OpenAI-based approach. (Implementation example.)
        /// </summary>
        /// <param name="content">The text to be cleaned by the OpenAI model.</param>
        /// <returns>A task with the cleaned text returned by the model.</returns>
        public async Task<string> CleanTextAsync(string content)
        {
            var response = await _openAIService.CreateChatCompletionTextAsync(
                [
                    SystemChatMessage.CreateSystemMessage(Promps.STemplateSystemPerson),
                    SystemChatMessage.CreateAssistantMessage(Promps.STemplateCleaner),
                    SystemChatMessage.CreateUserMessage(content)
                ]
            );

            return response;
        }

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private async Task<T> SafeExecuteAsync<T>(Task<T> task, string taskName, T defaultValue)
        {
            try
            {
                return await task;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in {taskName}: {ex.Message}");
                return defaultValue;
            }
        }
    }
}
