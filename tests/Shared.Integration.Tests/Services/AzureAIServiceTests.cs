using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.AI.TextAnalytics;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using Integration.Services;

namespace Shared.Integration.Tests.Services
{
    public class AzureAIServiceTests
    {
        private readonly Mock<TextAnalyticsClient> _mockTextAnalyticsClient;
        private readonly Mock<DocumentIntelligenceClient> _mockDocClient;
        private readonly Mock<ILogger<AzureAIService>> _mockLogger;
        private readonly AzureAIService _aiService;

        public AzureAIServiceTests()
        {
            _mockTextAnalyticsClient = new Mock<TextAnalyticsClient>();
            _mockDocClient = new Mock<DocumentIntelligenceClient>();
            _mockLogger = new Mock<ILogger<AzureAIService>>();

            // Instantiate AzureAIService with mock clients
            _aiService = new AzureAIService(
                _mockTextAnalyticsClient.Object,
                _mockDocClient.Object,
                _mockLogger.Object
            );
        }

        [Fact]
        public async Task ExtractKeyPhrasesAsync_ShouldReturnListOfKeyPhrases_WhenSuccessful()
        {
            // Arrange
            var content = "This is a test";
            var expectedPhrases = new List<string> { "test", "This" };

            // Use the model factory to create a real KeyPhraseCollection
            var realKeyPhraseCollection = TextAnalyticsModelFactory.KeyPhraseCollection(
                keyPhrases: expectedPhrases,
                warnings: new List<TextAnalyticsWarning>() // or any warnings you want
            );

            // Wrap the real KeyPhraseCollection in a Response<KeyPhraseCollection>
            var mockResponse = Response.FromValue(realKeyPhraseCollection, new Mock<Response>().Object);

            // Mock the ExtractKeyPhrasesAsync call to return our custom response
            _mockTextAnalyticsClient
                .Setup(c => c.ExtractKeyPhrasesAsync(content, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _aiService.ExtractKeyPhrasesAsync(content);

            // Assert
            result.Should().BeEquivalentTo(expectedPhrases);
        }


        [Fact]
        public async Task ExtractKeyPhrasesAsync_ShouldThrowInvalidOperationException_WhenRequestFailedExceptionOccurs()
        {
            // Arrange
            var content = "some content";
            _mockTextAnalyticsClient
                .Setup(c => c.ExtractKeyPhrasesAsync(content, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException("Extraction failed"));

            // Act
            Func<Task> act = async () => await _aiService.ExtractKeyPhrasesAsync(content);

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Failed to extract key phrases.*");
            // Optionally verify that the logger was called with LogError
        }

        [Fact]
        public async Task ExtractKeyPhrasesAsync_ShouldRethrow_WhenUnexpectedExceptionOccurs()
        {
            // Arrange
            var content = "some content";
            _mockTextAnalyticsClient
                .Setup(c => c.ExtractKeyPhrasesAsync(content, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Unexpected error"));

            // Act
            Func<Task> act = async () => await _aiService.ExtractKeyPhrasesAsync(content);

            // Assert
            await act.Should().ThrowAsync<Exception>().WithMessage("Unexpected error");
        }        

        [Fact]
        public async Task ExtractEntitiesAsync_ShouldReturnListOfEntities_WhenSuccessful()
        {
            // Arrange
            var content = "Microsoft was founded by Bill Gates.";

            // Create real CategorizedEntity objects via the factory
            var entity1 = TextAnalyticsModelFactory.CategorizedEntity(
                text: "Microsoft",
                category: "Organization",
                subCategory: null,
                score: 1.0
            );

            var entity2 = TextAnalyticsModelFactory.CategorizedEntity(
                text: "Bill Gates",
                category: "Person",
                subCategory: null,
                score: 1.0
            );

            // Create a real CategorizedEntityCollection
            var realEntityCollection = TextAnalyticsModelFactory.CategorizedEntityCollection(
                new[] { entity1, entity2 },
                new List<TextAnalyticsWarning>() // no warnings
            );

            // Wrap the collection in a Response<CategorizedEntityCollection>
            var mockResponse = Response.FromValue(realEntityCollection, new Mock<Response>().Object);

            // Mock the RecognizeEntitiesAsync call
            _mockTextAnalyticsClient
                .Setup(c => c.RecognizeEntitiesAsync(content, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _aiService.ExtractEntitiesAsync(content);

            // Assert
            result.Should().ContainInOrder("Microsoft", "Bill Gates");
        }


        [Fact]
        public async Task ExtractEntitiesAsync_ShouldThrowInvalidOperationException_WhenRequestFailedExceptionOccurs()
        {
            // Arrange
            var content = "some content";
            _mockTextAnalyticsClient
                .Setup(c => c.RecognizeEntitiesAsync(content, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException("Entities failed"));

            // Act
            Func<Task> act = async () => await _aiService.ExtractEntitiesAsync(content);

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Failed to extract entities.*");
        }

        [Fact]
        public async Task ExtractEntitiesAsync_ShouldRethrow_WhenUnexpectedExceptionOccurs()
        {
            // Arrange
            var content = "some content";
            _mockTextAnalyticsClient
                .Setup(c => c.RecognizeEntitiesAsync(content, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Unexpected error"));

            // Act
            Func<Task> act = async () => await _aiService.ExtractEntitiesAsync(content);

            // Assert
            await act.Should().ThrowAsync<Exception>().WithMessage("Unexpected error");
        }

       
        [Fact]
        public async Task AnalyzeDocumentAsync_ShouldReturnAnalyzeResult_WhenSuccessful()
        {
            // Arrange
            var content = BinaryData.FromString("fake document content");

            var fakeAnalyzeResult = DocumentIntelligenceModelFactory.AnalyzeResult(
                apiVersion: "2022-08-31",
                modelId: "fake-model-id"
            // etc.
            );

            var mockOperation = new Mock<Operation<AnalyzeResult>>();
            mockOperation.SetupGet(op => op.Value).Returns(fakeAnalyzeResult);

            // IMPORTANT: Use the 4-parameter overload to match your real code
            _mockDocClient
                .Setup(c => c.AnalyzeDocumentAsync(
                    WaitUntil.Completed,
                    "prebuilt-layout",
                    content,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockOperation.Object);

            // Act
            var result = await _aiService.AnalyzeDocumentAsync(content);

            // Assert
            result.Should().Be(fakeAnalyzeResult);
        }


        [Fact]
        public async Task AnalyzeDocumentAsync_ShouldThrowInvalidOperationException_WhenRequestFailedExceptionOccurs()
        {
            // Arrange
            var content = BinaryData.FromString("fake document content");

            // EXACTLY 4 parameters: (WaitUntil, string modelId, BinaryData, CancellationToken)
            _mockDocClient
                .Setup(c => c.AnalyzeDocumentAsync(
                    WaitUntil.Completed,
                    "prebuilt-layout",
                    content,
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException("Some error"));

            // Act
            Func<Task> act = async () => await _aiService.AnalyzeDocumentAsync(content);

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Failed to analyze the document.*");
        }


        [Fact]
        public async Task AnalyzeDocumentAsync_ShouldRethrow_WhenUnexpectedExceptionOccurs()
        {
            // Arrange
            var content = BinaryData.FromString("fake document content");

            // IMPORTANT: match the 4-parameter overload exactly
            _mockDocClient
                .Setup(c => c.AnalyzeDocumentAsync(
                    WaitUntil.Completed,
                    "prebuilt-layout",
                    content,
                    It.IsAny<CancellationToken>()))
                // Throw a normal Exception, so the "unexpected" catch block is hit
                .ThrowsAsync(new Exception("my unexpected error"));

            // Act
            Func<Task> act = async () => await _aiService.AnalyzeDocumentAsync(content);

            // Assert
            // The service rethrows the same exception, so we expect "my unexpected error"
            await act.Should().ThrowAsync<Exception>()
                .WithMessage("*my unexpected error*");
        }
    }
}
