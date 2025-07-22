using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Integration.Services;
using OpenAI.Chat;
using FluentAssertions;
using Integration.Interfaces;
using Castle.Core.Logging;
using System.Reflection;

namespace Shared.Integration.Tests.Services
{
    public class OpenAIServiceTests
    {
        private readonly Mock<IConfiguration> _mockConfiguration;
        private readonly Mock<ILogger<OpenAIService>> _mockLogger;
        private readonly OpenAIService _openAIService;

        public OpenAIServiceTests()
        {
            _mockConfiguration = new Mock<IConfiguration>();
            _mockLogger = new Mock<ILogger<OpenAIService>>();

            // Setup mock configuration values
            _mockConfiguration.Setup(config => config["OpenAI:Endpoint"]).Returns("https://test.openai.com");
            _mockConfiguration.Setup(config => config["OpenAI:Key1"]).Returns("test-key");
            _mockConfiguration.Setup(config => config["ConfigurationOpenAI:OpenAIModel"]).Returns("test-model");
            _mockConfiguration.Setup(config => config["ConfigurationOpenAI:OpenAIEmbeddingModel"]).Returns("test-embedding-model");
            _mockConfiguration.Setup(config => config["ConfigurationOpenAI:OpenAImaxTokens"]).Returns("100");
            _mockConfiguration.Setup(config => config["ConfigurationOpenAI:OpenAITemperature"]).Returns("0.7");
            _mockConfiguration.Setup(config => config["ConfigurationOpenAI:OpenAIFrequencyPenalty"]).Returns("0.5");
            _mockConfiguration.Setup(config => config["ConfigurationOpenAI:OpenAIPresencePenalty"]).Returns("0.2");
            _mockConfiguration.Setup(config => config["ConfigurationOpenAI:OpenAITopP"]).Returns("0.9");

            // Instantiate the service using IConfiguration
            _openAIService = new OpenAIService(
                _mockConfiguration.Object,
                _mockLogger.Object
            );
        }

        [Fact]
        public void Should_Initialize_Service_Correctly()
        {
            _openAIService.Should().NotBeNull();
        }

        [Fact]
        public void OpenAIService_ShouldThrowArgumentNullException_WhenLoggerIsNull()
        {
            var exception = Assert.Throws<ArgumentNullException>(() =>
                new OpenAIService(_mockConfiguration.Object, null!));
            Assert.Equal("logger", exception.ParamName);
        }

        [Fact]
        public void OpenAIService_ShouldThrowInvalidOperationException_WhenMissingConfiguration()
        {
            var invalidConfig = new Mock<IConfiguration>();
            var exception = Assert.Throws<InvalidOperationException>(() =>
                new OpenAIService(invalidConfig.Object, _mockLogger.Object));
            Assert.Contains("Missing", exception.Message);
        }

        [Fact]
        public async Task GetEmbeddingAsync_ShouldThrowInvalidOperationException_WhenClientIsNotInitialized()
        {
            // Forzamos que _clientAI sea null usando reflexión.
            var clientAIField = typeof(OpenAIService)
                .GetField("_clientAI", BindingFlags.NonPublic | BindingFlags.Instance);
            clientAIField?.SetValue(_openAIService, null);

            Func<Task> act = async () => await _openAIService.GetEmbeddingAsync("test-input");

            await act.Should()
                     .ThrowAsync<InvalidOperationException>()
                     .WithMessage("clientAI has not been initialized.");
        }


        [Fact]
        public async Task CreateChatCompletionTextAsync_ShouldThrowException_WhenChatClientIsNotInitialized()
        {
            var chatMessages = new List<ChatMessage>
            {
                CreateFakeChatMessage("Hello")
            };

            Func<Task> act = async () => await _openAIService.CreateChatCompletionTextAsync(chatMessages);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task CreateChatCompletionAsync_ShouldThrowException_WhenChatClientIsNotInitialized()
        {
            var chatMessages = new List<ChatMessage>
            {
                CreateFakeChatMessage("Hello")
            };

            Func<Task> act = async () => await _openAIService.CreateChatCompletionAsync(chatMessages);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        private ChatMessage CreateFakeChatMessage(string content)
        {
            var chatContent = new ChatMessageContent(content);
            return (ChatMessage)Activator.CreateInstance(typeof(ChatMessage), true)!;
        }

    }
}
