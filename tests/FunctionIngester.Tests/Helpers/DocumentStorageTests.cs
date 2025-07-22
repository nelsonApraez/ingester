using Entities;
using FunctionIngester.Helpers;
using Integration.Interfaces;
using Integration.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace FunctionIngester.Tests.Helpers
{
    public class DocumentStorageTests
    {
        private readonly Mock<IBlobStorageService> _blobStorageMock;
        private readonly Mock<IAzureAISearchService> _azureAISearchMock;
        private readonly Mock<ILogger<DocumentStorage>> _loggerMock;
        private readonly DocumentStorage _documentStorage;

        public DocumentStorageTests()
        {
            _blobStorageMock = new Mock<IBlobStorageService>();
            _azureAISearchMock = new Mock<IAzureAISearchService>();
            _loggerMock = new Mock<ILogger<DocumentStorage>>();
            _documentStorage = new DocumentStorage(_blobStorageMock.Object, _azureAISearchMock.Object, _loggerMock.Object);
        }

        [Fact]
        public async Task DownloadFileByUrlAsync_ReturnsBlobData_WhenDownloadSucceeds()
        {
            // Arrange: Set up test values and expected result.
            var blobUrl = new Uri("http://example.com/blob");
            var expectedBlobData = new BlobData();
            _blobStorageMock.Setup(x => x.DownloadFileByUrlAsync(blobUrl))
                            .ReturnsAsync(expectedBlobData);

            // Act: Call the method under test.
            var result = await _documentStorage.DownloadFileByUrlAsync(blobUrl);

            // Assert: Verify that the expected result is returned.
            Assert.Equal(expectedBlobData, result);
        }

        [Fact]
        public async Task DownloadFileByUrlAsync_ThrowsInvalidOperationException_WhenDownloadFails()
        {
            // Arrange: Set up the download to throw an exception.
            var blobUrl = new Uri("http://example.com/blob");
            var innerException = new Exception("Download failed");
            _blobStorageMock.Setup(x => x.DownloadFileByUrlAsync(blobUrl))
                            .ThrowsAsync(innerException);

            // Act & Assert: Verify that an InvalidOperationException is thrown.
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _documentStorage.DownloadFileByUrlAsync(blobUrl));
            Assert.Contains("Error downloading the blob", exception.Message);
            Assert.Equal(innerException, exception.InnerException);
        }

        [Fact]
        public async Task UploadFilesToStorageAsync_UploadsOnlyValidChunks()
        {
            // Arrange
            var validChunk = new DocumentChunk
            {
                ChunkPath = "validPath",
                JsonContent = "{\"data\":\"value\"}"
            };

            var chunkWithoutPath = new DocumentChunk
            {
                ChunkPath = string.Empty,
                JsonContent = "{\"data\":\"value\"}"
            };

            var chunkWithoutJson = new DocumentChunk
            {
                ChunkPath = "noJsonPath",
                JsonContent = string.Empty
            };

            var chunksList = new List<DocumentChunk>
            {
                validChunk,
                chunkWithoutPath,
                chunkWithoutJson
            };

            // Act
            await _documentStorage.UploadFilesToStorageAsync(chunksList);

            // Assert
            _blobStorageMock.Verify(x => x.UploadJsonContentAsync(
                    It.Is<string>(json => json == validChunk.JsonContent),
                    It.Is<string>(blob => blob == validChunk.ChunkPath),
                    true),
                Times.Once);

            // Verify that chunkWithoutPath is not uploaded
            _blobStorageMock.Verify(x => x.UploadJsonContentAsync(
                    It.IsAny<string>(),
                    It.Is<string>(blob => blob == chunkWithoutPath.ChunkPath),
                    true),
                Times.Never);

            // Verify that chunkWithoutJson is not uploaded
            _blobStorageMock.Verify(x => x.UploadJsonContentAsync(
                    It.Is<string>(json => json == chunkWithoutJson.JsonContent),
                    It.IsAny<string>(),
                    true),
                Times.Never);
        }


        [Fact]
        public async Task MoveBlob_CallsBlobStorageService_AndLogsInformation()
        {
            // Arrange: Define blob name and folder names.
            var blobName = "file1";
            var sourceFolder = "source";
            var destinationFolder = "destination";

            // Act: Call the method to move the blob.
            await _documentStorage.MoveBlob(blobName, sourceFolder, destinationFolder);

            // Assert: Verify that the move method on the blob storage service was called once.
            _blobStorageMock.Verify(x => x.MoveBlobAsync(blobName, sourceFolder, destinationFolder), Times.Once);
        }

        [Fact]
        public void RunIndexer_CallsAzureAISearchService_RunIndexerAsync()
        {
            // Act: Execute the RunIndexer method.
            _documentStorage.RunIndexer();

            // Assert: Verify that the RunIndexerAsync method was called on the Azure AI Search service.
            _azureAISearchMock.Verify(x => x.RunIndexerAsync(), Times.Once);
        }

        [Fact]
        public async Task UploadFilesToAISearch_ReturnsFalse_WhenChunksListIsNullOrEmpty()
        {
            // Arrange & Act: Test with a null list.
            List<DocumentChunk> nullList = null;
            var result = await _documentStorage.UploadFilesToAISearch(nullList);
            Assert.False(result);

            // Act: Test with an empty list.
            var emptyList = new List<DocumentChunk>();
            result = await _documentStorage.UploadFilesToAISearch(emptyList);
            Assert.False(result);
        }

        [Fact]
        public async Task UploadFilesToAISearch_ReturnsTrue_WhenUploadSucceeds()
        {
            // Arrange: Set up a valid chunks list and simulate a successful upload.
            var chunksList = new List<DocumentChunk>
            {
                new DocumentChunk
                {
                    ChunkPath = "path1",
                    JsonContent = "{\"data\":\"value\"}"
                }
            };

            _azureAISearchMock.Setup(x => x.UploadFilesAsync(chunksList))
                              .ReturnsAsync(true);

            // Act: Call the method under test.
            var result = await _documentStorage.UploadFilesToAISearch(chunksList);

            // Assert: Verify that the method returns true and the upload method was called.
            Assert.True(result);
            _azureAISearchMock.Verify(x => x.UploadFilesAsync(chunksList), Times.Once);
        }

        [Fact]
        public async Task UploadFilesToAISearch_ReturnsFalse_WhenUploadFails()
        {
            // Arrange: Set up a chunks list and simulate a failed upload.
            var chunksList = new List<DocumentChunk>
            {
                new DocumentChunk { ChunkPath = "path1", JsonContent = "{\"data\":\"value\"}" }
            };

            _azureAISearchMock.Setup(x => x.UploadFilesAsync(chunksList))
                              .ReturnsAsync(false);

            // Act: Call the method.
            var result = await _documentStorage.UploadFilesToAISearch(chunksList);

            // Assert: Verify that the method returns false.
            Assert.False(result);
            _azureAISearchMock.Verify(x => x.UploadFilesAsync(chunksList), Times.Once);
        }

        [Fact]
        public async Task UploadFilesToAISearch_ThrowsException_WhenServiceFails()
        {
            // Arrange: Set up a chunks list and simulate an exception during upload.
            var chunksList = new List<DocumentChunk>
            {
                new DocumentChunk { ChunkPath = "path1", JsonContent = "{\"data\":\"value\"}" }
            };
            var simulatedException = new Exception("Upload error");
            _azureAISearchMock.Setup(x => x.UploadFilesAsync(chunksList))
                              .ThrowsAsync(simulatedException);

            // Act & Assert: Verify that an exception is thrown with the expected message.
            var ex = await Assert.ThrowsAsync<Exception>(() => _documentStorage.UploadFilesToAISearch(chunksList));
            Assert.Equal("Upload error", ex.Message);
        }
    }

}
