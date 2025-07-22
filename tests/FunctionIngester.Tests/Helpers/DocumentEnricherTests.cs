using Entities;
using FunctionIngester.Helpers;
using Integration.Interfaces;
using Integration.Services;
using Microsoft.Extensions.Logging;
using Moq;
using OpenAI.Chat;
using Xunit;

namespace FunctionIngester.Tests.Helpers
{
    public class DocumentEnricherTests
    {
        private readonly Mock<IAzureAIService> _mockAzureAIService;
        private readonly Mock<IOpenAIService> _mockOpenAIService;
        private readonly Mock<ILogger<DocumentEnricher>> _mockLogger;
        private readonly DocumentEnricher _documentEnricher;

        public DocumentEnricherTests()
        {
            _mockAzureAIService = new Mock<IAzureAIService>();
            _mockOpenAIService = new Mock<IOpenAIService>();
            _mockLogger = new Mock<ILogger<DocumentEnricher>>();
            _documentEnricher = new DocumentEnricher(_mockAzureAIService.Object, _mockOpenAIService.Object, _mockLogger.Object);
        }

        [Fact]
        public async Task EnrichDocumentAsync_ValidInput_ShouldReturnEnrichedChunks()
        {
            // Arrange
            var chunkPath = "chunk1";
            var originalContent = "Test content for enrichment.";
            var initialJson = $"{{ \"chunk_file\": \"chunk1.txt\", \"content\": \"{originalContent}\" }}";
            var chunks = new List<DocumentChunk> { new DocumentChunk { ChunkPath = chunkPath, JsonContent = initialJson } };

            _mockOpenAIService.Setup(s => s.CreateChatCompletionTextAsync(It.IsAny<List<ChatMessage>>(), "text"))
                .ReturnsAsync("Cleaned " + originalContent);
            _mockAzureAIService.Setup(s => s.ExtractKeyPhrasesAsync(It.IsAny<string>())).ReturnsAsync(new List<string> { "Test", "Enrichment" });
            _mockAzureAIService.Setup(s => s.ExtractEntitiesAsync(It.IsAny<string>())).ReturnsAsync(new List<string> { "Entity1", "Entity2" });
            _mockOpenAIService.Setup(s => s.GetEmbeddingAsync(It.IsAny<string>())).ReturnsAsync(new List<float> { 0.1f, 0.2f, 0.3f });

            // Act
            var result = await _documentEnricher.EnrichDocumentAsync(chunks);

            // Assert
            Assert.Single(result);
            Assert.Contains("keyphrases", result[0].JsonContent);
            Assert.Contains("entities", result[0].JsonContent);
            Assert.Contains("context", result[0].JsonContent);
            Assert.Contains("contentVector", result[0].JsonContent);
        }

        [Fact]
        public async Task EnrichDocumentAsync_NullChunks_ShouldThrowArgumentException()
        {
            // Arrange
            List<DocumentChunk> chunks = null;

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => _documentEnricher.EnrichDocumentAsync(chunks));

            _mockLogger.Verify(l => l.Log(
                LogLevel.Error, It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("The chunks list is null or empty")),
                It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }

        [Fact]
        public async Task EnrichDocumentAsync_EmptyChunks_ShouldThrowArgumentException()
        {
            // Arrange
            var chunks = new List<DocumentChunk>();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => _documentEnricher.EnrichDocumentAsync(chunks));
        }

        [Fact]
        public async Task EnrichDocumentAsync_InvalidJsonContent_ShouldThrowArgumentException()
        {
            // Arrange
            var chunkPath = "chunk1";
            var invalidJson = "Not a JSON";
            var chunks = new List<DocumentChunk> { new DocumentChunk { ChunkPath = chunkPath, JsonContent = invalidJson } };

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => _documentEnricher.EnrichDocumentAsync(chunks));

            _mockLogger.Verify(l => l.Log(
                LogLevel.Error, It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Invalid JSON content for chunk")),
                It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }

        [Fact]
        public async Task EnrichDocumentAsync_EmptyChunkPath_ShouldThrowArgumentException()
        {
            // Arrange
            var chunks = new List<DocumentChunk> { new DocumentChunk { ChunkPath = "", JsonContent = "{}" } };

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => _documentEnricher.EnrichDocumentAsync(chunks));
        }

        [Fact]
        public async Task EnrichDocumentAsync_MissingContentProperty_ShouldThrowArgumentException()
        {
            // Arrange
            var chunkPath = "chunk1";
            var jsonWithoutContent = "{ \"chunk_file\": \"chunk1.txt\" }";
            var chunks = new List<DocumentChunk> { new DocumentChunk { ChunkPath = chunkPath, JsonContent = jsonWithoutContent } };

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => _documentEnricher.EnrichDocumentAsync(chunks));
        }

        [Fact]
        public async Task EnrichDocumentAsync_OpenAIServiceFails_ShouldContinueWithPartialData()
        {
            // Arrange
            var chunkPath = "chunk1";
            var originalContent = "Test content for enrichment.";
            var initialJson = $"{{ \"chunk_file\": \"chunk1.txt\", \"content\": \"{originalContent}\" }}";
            var chunks = new List<DocumentChunk> { new DocumentChunk { ChunkPath = chunkPath, JsonContent = initialJson } };

            _mockOpenAIService.Setup(s => s.CreateChatCompletionTextAsync(It.IsAny<List<ChatMessage>>(), "text"))
                .ThrowsAsync(new Exception("OpenAI error"));
            _mockAzureAIService.Setup(s => s.ExtractKeyPhrasesAsync(It.IsAny<string>())).ReturnsAsync(new List<string> { "Test", "Enrichment" });

            // Act
            var result = await _documentEnricher.EnrichDocumentAsync(chunks);

            // Assert
            Assert.Single(result);
            Assert.Contains("keyphrases", result[0].JsonContent);
            Assert.Contains("entities", result[0].JsonContent);
            Assert.DoesNotContain("context", result[0].JsonContent);
        }

        [Fact]
        public async Task ExtractOpenAIContextAsync_ShouldReturnResponse()
        {
            // Arrange
            string content = "Test content";
            _mockOpenAIService.Setup(s => s.CreateChatCompletionTextAsync(It.IsAny<List<ChatMessage>>(), "text"))
                .ReturnsAsync("Context extracted");

            // Act
            var result = await _documentEnricher.ExtractOpenAIContextAsync(content);

            // Assert
            Assert.Equal("Context extracted", result);
        }

        [Fact]
        public async Task ExtractOpenAIContentVectorAsync_ShouldReturnEmbeddingVector()
        {
            // Arrange
            string content = "Test content";
            var expectedVector = new List<float> { 0.1f, 0.2f, 0.3f };
            _mockOpenAIService.Setup(s => s.GetEmbeddingAsync(It.IsAny<string>())).ReturnsAsync(expectedVector);

            // Act
            var result = await _documentEnricher.ExtractOpenAIContentVectorAsync(content);

            // Assert
            Assert.Equal(expectedVector, result);
        }

        [Fact]
        public async Task CleanTextAsync_ShouldReturnCleanedText()
        {
            // Arrange
            string inputText = "Unclean text with noise.";
            _mockOpenAIService.Setup(s => s.CreateChatCompletionTextAsync(It.IsAny<List<ChatMessage>>(), "text"))
                .ReturnsAsync("Cleaned text");

            // Act
            var result = await _documentEnricher.CleanTextAsync(inputText);

            // Assert
            Assert.Equal("Cleaned text", result);
        }
    }
}
