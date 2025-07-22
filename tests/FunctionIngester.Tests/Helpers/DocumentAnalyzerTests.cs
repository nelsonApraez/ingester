using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using FunctionIngester.Helpers;
using FunctionIngester.Helpers.Interfaces;
using Integration.Services;
using Azure.AI.DocumentIntelligence;
using FunctionIngester.Utils.Interfaces;

namespace FunctionIngester.Tests.Helpers
{
    public class DocumentAnalyzerTests
    {
        private readonly Mock<IAzureAIService> _mockAzureAiService;
        private readonly Mock<IAnalyzerUtil> _mockAnalyzerUtil;
        private readonly Mock<ILogger<DocumentAnalyzer>> _mockLogger;
        private readonly IDocumentAnalyzer _documentAnalyzer;

        public DocumentAnalyzerTests()
        {
            _mockAzureAiService = new Mock<IAzureAIService>();
            _mockAnalyzerUtil = new Mock<IAnalyzerUtil>(); 
            _mockLogger = new Mock<ILogger<DocumentAnalyzer>>();            
            _documentAnalyzer = new DocumentAnalyzer(_mockAzureAiService.Object, _mockAnalyzerUtil.Object, _mockLogger.Object);
        }


        private BlobData CreateValidBlobData(
            string name = "folder/document.pdf",
            byte[]? contentBytes = null,
            string? uri = "https://fake.blob.core.windows.net/folder/document.pdf")
        {
            contentBytes ??= new byte[] { 0x1, 0x2, 0x3 };
            return new BlobData
            {
                Name = name,
                Content = new BinaryData(contentBytes),
                Uri = uri is null ? null : new Uri(uri)
            };
        }

        /// <summary>
        /// Ensures that AnalyzeDocumentAsync throws ArgumentException when BlobData is missing required fields.
        /// </summary>
        [Fact]
        public async Task AnalyzeDocumentAsync_ShouldThrowArgumentException_WhenBlobDataIsMissingFields()
        {
            // Arrange
            var blobData = CreateValidBlobData();
            blobData.Name = null; // invalid => triggers ArgumentException

            // Act
            Func<Task> act = async () => await _documentAnalyzer.AnalyzeDocumentAsync(blobData);

            // Assert
            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("*cannot be null or empty*");
        }

        /// <summary>
        /// Ensures that AnalyzeDocumentAsync returns null when the AI service returns null.
        /// </summary>
        [Fact]
        public async Task AnalyzeDocumentAsync_ShouldReturnNull_WhenAnalyzeDocumentAsyncReturnsNull()
        {
            // Arrange
            var blobData = CreateValidBlobData();

            _mockAzureAiService
                .Setup(s => s.AnalyzeDocumentAsync(It.IsAny<BinaryData>()))
                .ReturnsAsync((AnalyzeResult)null!);

            // Act
            var result = await _documentAnalyzer.AnalyzeDocumentAsync(blobData);

            // Assert
            result.Should().BeNull("because the AI service returned null");

            // Verify that a warning was logged
            _mockLogger.Verify(
                l => l.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => Convert.ToString(v)!.Contains($"Analysis result is null for {blobData.Name}.")),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ), Times.Once);
        }

        /// <summary>
        /// Ensures that AnalyzeDocumentAsync returns null when an exception occurs during analysis.
        /// </summary>
        [Fact]
        public async Task AnalyzeDocumentAsync_ShouldReturnNull_WhenExceptionOccurs()
        {
            // Arrange
            var blobData = CreateValidBlobData();

            _mockAzureAiService
                .Setup(s => s.AnalyzeDocumentAsync(It.IsAny<BinaryData>()))
                .ThrowsAsync(new Exception("Unexpected error"));

            // Act
            var result = await _documentAnalyzer.AnalyzeDocumentAsync(blobData);

            // Assert
            result.Should().BeNull("because an exception was thrown and caught");

            // Verify that an error was logged
            _mockLogger.Verify(
                l => l.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => Convert.ToString(v)!.Contains($"Error analyzing blob {blobData.Name}: Unexpected error.")),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ), Times.Once);
        }
    }
}
