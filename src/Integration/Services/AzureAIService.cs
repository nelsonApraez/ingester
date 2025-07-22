using Azure;
using Azure.AI.TextAnalytics;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace Integration.Services
{    
    public class AzureAIService : IAzureAIService
    {
        private readonly TextAnalyticsClient _textAnalyticsClient;
        private readonly DocumentIntelligenceClient _documentAnalysisClient;
        private readonly ILogger<AzureAIService> _logger;
       
        public AzureAIService(IConfiguration configuration, ILogger<AzureAIService> logger)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            var endpoint = configuration["AzureAI:Endpoint"]
                ?? throw new InvalidOperationException("Missing AzureAI:Endpoint in configuration.");

            var secretName = configuration["AzureAI:Key1:SecretName"];

            var apiKey = !string.IsNullOrWhiteSpace(secretName)
                ? configuration[secretName] ?? configuration["AzureAI:Key1"]
                : configuration["AzureAI:Key1"]
                ?? throw new InvalidOperationException("Missing AzureAI:Key1 in configuration.");

            try
            {
                var credentials = new AzureKeyCredential(apiKey!);
                _textAnalyticsClient = new TextAnalyticsClient(new Uri(endpoint), credentials);
                _documentAnalysisClient = new DocumentIntelligenceClient(new Uri(endpoint), credentials);
                _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to initialize AzureAIService.", ex);
            }
        }

        public AzureAIService(TextAnalyticsClient textAnalyticsClient, DocumentIntelligenceClient documentAnalysisClient, ILogger<AzureAIService> logger)
        {
            _textAnalyticsClient = textAnalyticsClient ?? throw new ArgumentNullException(nameof(textAnalyticsClient));
            _documentAnalysisClient = documentAnalysisClient ?? throw new ArgumentNullException(nameof(documentAnalysisClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Extracts key phrases from text content.
        /// </summary>
        /// <param name="content">The text to analyze.</param>
        /// <returns>A task representing the asynchronous operation, containing a list of extracted key phrases.</returns>
        public async Task<List<string>> ExtractKeyPhrasesAsync(string content)
        {
            try
            {
                var response = await _textAnalyticsClient.ExtractKeyPhrasesAsync(content);
                return new List<string>(response.Value);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError("Error extracting key phrases: {ErrorMessage}", ex.Message);
                throw new InvalidOperationException("Failed to extract key phrases.", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError("Unexpected error extracting key phrases: {ErrorMessage}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Extracts entities from text content.
        /// </summary>
        /// <param name="content">The text to analyze.</param>
        /// <returns>A task representing the asynchronous operation, containing a list of recognized entities.</returns>
        public async Task<List<string>> ExtractEntitiesAsync(string content)
        {
            try
            {
                var response = await _textAnalyticsClient.RecognizeEntitiesAsync(content);
                var entities = new List<string>();
                foreach (var entity in response.Value)
                {
                    entities.Add(entity.Text);
                }
                return entities;
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError("Error extracting entities: {ErrorMessage}", ex.Message);
                throw new InvalidOperationException("Failed to extract entities.", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError("Unexpected error extracting entities: {ErrorMessage}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Analyzes a document using Document Intelligence.
        /// </summary>
        /// <param name="base64Source">The binary data representing the document to analyze.</param>
        /// <returns>A task representing the asynchronous operation, containing the analysis result.</returns>
        public async Task<AnalyzeResult> AnalyzeDocumentAsync(BinaryData content)
        {
            if (_documentAnalysisClient == null)
            {
                throw new InvalidOperationException("DocumentIntelligenceClient has not been initialized.");
            }

            try
            {                
                var operation = await _documentAnalysisClient.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-layout", content);
                return operation.Value;
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError("Error analyzing document: {ErrorMessage}", ex.Message);
                throw new InvalidOperationException("Failed to analyze the document.", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError("Unexpected error analyzing document: {ErrorMessage}", ex.Message);
                throw;
            }
        }
    }
}
