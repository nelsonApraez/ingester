using Integration.Interfaces;
using Azure.AI.OpenAI;
using OpenAI.Chat;
using Microsoft.Extensions.Logging;
using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Integration.Services
{
    public class OpenAIService : IOpenAIService
    {
        private readonly int? _openAIMaxTokens;
        private readonly float _openAITemperature;
        private readonly float _openAIFrequencyPenalty;
        private readonly float _openAIPresencePenalty;
        private readonly float _openAITopP;
        private string? _openAIModel;
        private string? _openAIModelEmbedding;
        private AzureOpenAIClient? _clientAI;
        private ChatClient? _chatClient;
        private readonly ILogger<OpenAIService> _logger;
        
        public OpenAIService(IConfiguration configuration, ILogger<OpenAIService> logger)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            var apiManagementEndpoint = configuration["OpenAI:Endpoint"]
                ?? throw new InvalidOperationException("Missing OpenAI:Endpoint in configuration.");

            var secretName = configuration["OpenAI:Key1:SecretName"];

            var apimSubscriptionKey = !string.IsNullOrWhiteSpace(secretName)
                ? configuration[secretName] ?? configuration["OpenAI:Key1"]
                : configuration["OpenAI:Key1"]
                ?? throw new InvalidOperationException("Missing OpenAI:Key1 in configuration.");


            _openAIModel = configuration["ConfigurationOpenAI:OpenAIModel"]
                ?? throw new InvalidOperationException("Missing ConfigurationOpenAI:OpenAIModel in configuration.");

            _openAIModelEmbedding = configuration["ConfigurationOpenAI:OpenAIEmbeddingModel"]
                ?? throw new InvalidOperationException("Missing ConfigurationOpenAI:OpenAIEmbeddingModel in configuration.");

            _openAIMaxTokens = int.TryParse(configuration["ConfigurationOpenAI:OpenAImaxTokens"], out var maxTokens) ? maxTokens : null;
            _openAITemperature = float.TryParse(configuration["ConfigurationOpenAI:OpenAITemperature"], NumberStyles.Float, CultureInfo.InvariantCulture, out var temp) ? temp : 0;
            _openAIFrequencyPenalty = float.TryParse(configuration["ConfigurationOpenAI:OpenAIFrequencyPenalty"], NumberStyles.Float, CultureInfo.InvariantCulture, out var freqPenalty) ? freqPenalty : 0;
            _openAIPresencePenalty = float.TryParse(configuration["ConfigurationOpenAI:OpenAIPresencePenalty"], NumberStyles.Float, CultureInfo.InvariantCulture, out var presPenalty) ? presPenalty : 0;
            _openAITopP = float.TryParse(configuration["ConfigurationOpenAI:OpenAITopP"], NumberStyles.Float, CultureInfo.InvariantCulture, out var topP) ? topP : 0;

            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            try
            {
                // Call SetConfiguration to initialize clients
                SetConfiguration(apiManagementEndpoint, apimSubscriptionKey!, _openAIModel, _openAIModelEmbedding);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to initialize OpenAIService.", ex);
            }
        }

        /// <summary>
        /// Initializes OpenAIService using injected clients (for Dependency Injection and testing).
        /// </summary>
        public OpenAIService(AzureOpenAIClient clientAI, ChatClient chatClient, ILogger<OpenAIService> logger)
        {
            _clientAI = clientAI ?? throw new ArgumentNullException(nameof(clientAI));
            _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Sets the configuration for the OpenAIService.
        /// </summary>
        /// <param name="urlAPIM">The URL for the OpenAI service.</param>
        /// <param name="apimSubscriptionKey">The API key for authentication.</param>
        /// <param name="modelDep">Optional parameter to override the default model.</param>
        /// <param name="modelDepEmbedding">Optional parameter to override the embedding model.</param>
        public void SetConfiguration(string urlAPIM, string apimSubscriptionKey, string modelDep = "", string modelDepEmbedding = "")
        {
            try
            {
                _openAIModel = string.IsNullOrEmpty(modelDep) ? _openAIModel : modelDep;
                _openAIModelEmbedding = string.IsNullOrEmpty(modelDepEmbedding) ? _openAIModelEmbedding : modelDepEmbedding;

                _clientAI = new AzureOpenAIClient(new Uri(urlAPIM), new System.ClientModel.ApiKeyCredential(apimSubscriptionKey));
                _chatClient = _clientAI.GetChatClient(_openAIModel);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error configuring OpenAIService: {ErrorMessage}", ex.Message);
                throw new InvalidOperationException("Failed to configure OpenAIService.", ex);
            }
        }

        /// <summary>
        /// Creates a chat completion response in plain text format based on the provided list of chat messages.
        /// </summary>
        public async Task<string> CreateChatCompletionTextAsync(IList<ChatMessage> requests, string modelFormat = "text")
        {
            try
            {
                var result = await CreateChatCompletionAsync(requests, modelFormat).ConfigureAwait(false);
                return result.Content.First().Text;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error creating chat completion text: {ErrorMessage}", ex.Message);
                throw new InvalidOperationException("Failed to create chat completion text.", ex);
            }
        }

        /// <summary>
        /// Creates a chat completion response as a full object, including metadata and additional information.
        /// </summary>
        public async Task<ChatCompletion> CreateChatCompletionAsync(IList<ChatMessage> requests, string modelFormat = "text")
        {

            if (_chatClient == null)
            {
                throw new InvalidOperationException("Chat client has not been initialized.");
            }

            try
            {
                ChatCompletion completion = await _chatClient.CompleteChatAsync(requests,
                  new ChatCompletionOptions
                  {
                      Temperature = _openAITemperature,
                      FrequencyPenalty = _openAIFrequencyPenalty,
                      PresencePenalty = _openAIPresencePenalty,
                      MaxOutputTokenCount = _openAIMaxTokens,
                      TopP = _openAITopP,
                      ResponseFormat = modelFormat != "text" ? ChatResponseFormat.CreateJsonObjectFormat() : ChatResponseFormat.CreateTextFormat()
                  }
                );
                return completion;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error creating chat completion: {ErrorMessage}", ex.Message);
                throw new InvalidOperationException("Failed to create chat completion.", ex);
            }
        }

        /// <summary>
        /// Generates an embedding vector for a given input string using the specified embedding model.
        /// </summary>
        public async Task<IList<float>?> GetEmbeddingAsync(string input)
        {

            if (_clientAI == null)
            {
                throw new InvalidOperationException("clientAI has not been initialized.");
            }

            try
            {
                var embeddingClient = _clientAI.GetEmbeddingClient(_openAIModelEmbedding);
                var response = await embeddingClient.GenerateEmbeddingAsync(input).ConfigureAwait(false);

                // Validate the response and ensure no null values
                if (response?.Value == null)
                {
                    return null;
                }

                // Use ToFloats() directly if it's available
                return response.Value.ToFloats().ToArray().ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError("Error generating embedding for input: {ErrorMessage}", ex.Message);
                throw new InvalidOperationException("Failed to generate embedding.", ex);
            }
        }
    }
}