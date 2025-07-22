using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Integration.Services;
using Entities;

namespace Shared.Integration.Tests.Services
{
    public class AzureAISearchServiceTests
    {
        private readonly Mock<IConfiguration> _mockConfiguration;
        private readonly Mock<ILogger<AzureAISearchService>> _mockLogger;
        private readonly AzureAISearchService _searchService;

        public AzureAISearchServiceTests()
        {
            _mockConfiguration = new Mock<IConfiguration>();
            _mockLogger = new Mock<ILogger<AzureAISearchService>>();

            // Setup mock configuration values
            _mockConfiguration.Setup(config => config["AzureAISearch:Endpoint"])
                .Returns("https://test.search.windows.net");
            _mockConfiguration.Setup(config => config["AzureAISearch:Key1"])
                .Returns("test-key");
            _mockConfiguration.Setup(config => config["AzureAISearch:IndexName"])
                .Returns("test-index");

            // Instantiate the service using IConfiguration
            _searchService = new AzureAISearchService(
                _mockConfiguration.Object,
                _mockLogger.Object
            );
        }

        [Fact]
        public void Should_Initialize_Service_Correctly()
        {
            _searchService.Should().NotBeNull();
        }

        [Fact]
        public async Task RunIndexerAsync_ShouldThrowException_WhenIndexerFails()
        {
            Func<Task> act = async () => await _searchService.RunIndexerAsync();
            await act.Should().ThrowAsync<RequestFailedException>();
        }        

        [Fact]
        public async Task UploadFilesAsync_ShouldThrowException_WhenJsonIsInvalid()
        {
            var testChunks = new List<DocumentChunk>
            {
                new DocumentChunk { ChunkPath = "invalid", JsonContent = "{ invalid-json }" }
            };

            Func<Task> act = async () => await _searchService.UploadFilesAsync(testChunks);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task UploadFilesAsync_ShouldThrowException_WhenRequestFails()
        {
            var testChunks = new List<DocumentChunk>
            {
                new DocumentChunk { ChunkPath = "path1", JsonContent = "{ \"file_name\": \"File1\" }" }
            };

            Func<Task> act = async () => await _searchService.UploadFilesAsync(testChunks);
            await act.Should().ThrowAsync<RequestFailedException>();
        }

        [Fact]
        public async Task UploadFilesAsync_ShouldThrowException_WhenUnexpectedErrorOccurs()
        {
            var testChunks = new List<DocumentChunk>
            {
                new DocumentChunk { ChunkPath = "path1", JsonContent = "{ \"file_name\": \"File1\" }" }
            };

            Func<Task> act = async () => await _searchService.UploadFilesAsync(testChunks);
            await act.Should().ThrowAsync<Exception>();
        }        
    }
}
