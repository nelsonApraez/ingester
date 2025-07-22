using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using Integration.Services;

namespace Shared.Integration.Tests.Services
{
    public class BlobStorageServiceTests
    {
        private readonly Mock<BlobContainerClient> _mockContainerClient;
        private readonly Mock<BlobClient> _mockBlobClient;
        private readonly Mock<ILogger<BlobStorageService>> _mockLogger;
        private readonly BlobStorageService _blobStorageService;

        public BlobStorageServiceTests()
        {
            _mockContainerClient = new Mock<BlobContainerClient>();
            _mockBlobClient = new Mock<BlobClient>();
            _mockLogger = new Mock<ILogger<BlobStorageService>>();

            // Instantiate the service with the mocked BlobContainerClient
            _blobStorageService = new BlobStorageService(_mockContainerClient.Object, _mockLogger.Object);
        }

        [Fact]
        public void Should_Initialize_Service_Correctly()
        {
            // Ensures the service is not null.
            _blobStorageService.Should().NotBeNull();
        }

        [Fact]
        public async Task MoveBlobAsync_ShouldThrowFileNotFoundException_WhenSourceBlobDoesNotExist()
        {
            // Arrange
            string blobName = "test.txt";
            string sourceFolder = "source";
            string destinationFolder = "destination";

            var mockSourceBlobClient = new Mock<BlobClient>();
            var mockDestinationBlobClient = new Mock<BlobClient>();

            _mockContainerClient
                .Setup(c => c.GetBlobClient($"{sourceFolder}/{blobName}"))
                .Returns(mockSourceBlobClient.Object);
            _mockContainerClient
                .Setup(c => c.GetBlobClient($"{destinationFolder}/{blobName}"))
                .Returns(mockDestinationBlobClient.Object);

            // Source blob does not exist
            mockSourceBlobClient
                .Setup(b => b.ExistsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(false, new Mock<Response>().Object));

            // Act
            Func<Task> act = async () => await _blobStorageService.MoveBlobAsync(blobName, sourceFolder, destinationFolder);

            // Assert
            await act.Should().ThrowAsync<FileNotFoundException>()
                .WithMessage($"Source blob '{blobName}' not found in folder '{sourceFolder}'.");
        }

        [Fact]
        public async Task MoveBlobAsync_ShouldMoveBlob_WhenSourceBlobExists()
        {
            // Arrange
            string blobName = "test.txt";
            string sourceFolder = "source";
            string destinationFolder = "destination";

            var mockSourceBlobClient = new Mock<BlobClient>();
            var mockDestinationBlobClient = new Mock<BlobClient>();

            _mockContainerClient
                .Setup(c => c.GetBlobClient($"{sourceFolder}/{blobName}"))
                .Returns(mockSourceBlobClient.Object);
            _mockContainerClient
                .Setup(c => c.GetBlobClient($"{destinationFolder}/{blobName}"))
                .Returns(mockDestinationBlobClient.Object);

            // Source blob exists
            mockSourceBlobClient
                .Setup(b => b.ExistsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(true, new Mock<Response>().Object));

            // Mock the CopyFromUriOperation
            var mockCopyFromUriOperation = new Mock<CopyFromUriOperation>();

            // Set up the 7-parameter overload
            mockDestinationBlobClient
                .Setup(b => b.StartCopyFromUriAsync(
                    It.IsAny<Uri>(),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<AccessTier?>(),
                    It.IsAny<BlobRequestConditions>(),
                    It.IsAny<BlobRequestConditions>(),
                    It.IsAny<RehydratePriority?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCopyFromUriOperation.Object);

            // Mock DeleteAsync
            mockSourceBlobClient
                .Setup(b => b.DeleteAsync(
                    It.IsAny<DeleteSnapshotsOption>(),
                    It.IsAny<BlobRequestConditions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Mock<Response>().Object);

            // Act
            await _blobStorageService.MoveBlobAsync(blobName, sourceFolder, destinationFolder);

            // Assert
            mockDestinationBlobClient.Verify(b => b.StartCopyFromUriAsync(
                It.IsAny<Uri>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<AccessTier?>(),
                It.IsAny<BlobRequestConditions>(),
                It.IsAny<BlobRequestConditions>(),
                It.IsAny<RehydratePriority?>(),
                It.IsAny<CancellationToken>()),
                Times.Once);

            mockSourceBlobClient.Verify(b => b.DeleteAsync(
                It.IsAny<DeleteSnapshotsOption>(),
                It.IsAny<BlobRequestConditions>(),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task UploadFileAsync_ShouldThrowArgumentException_WhenFileIsNullOrEmpty()
        {
            // Arrange
            byte[] emptyFile = Array.Empty<byte>();
            string fileName = "upload.txt";

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => _blobStorageService.UploadFileAsync(emptyFile, fileName, overwrite: true));
        }

        [Fact]
        public async Task UploadFileAsync_ShouldThrowArgumentException_WhenFileNameIsNullOrEmpty()
        {
            // Arrange
            byte[] fileContent = new byte[] { 1, 2, 3 };
            string fileName = "   "; // whitespace

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => _blobStorageService.UploadFileAsync(fileContent, fileName, overwrite: true));
        }

        [Fact]
        public async Task UploadFileAsync_ShouldThrowInvalidOperationException_WhenFileExistsAndOverwriteIsFalse()
        {
            // Arrange
            byte[] fileContent = new byte[] { 1, 2, 3 };
            string fileName = "existing.txt";

            var mockBlobClient = new Mock<BlobClient>();
            _mockContainerClient.Setup(c => c.GetBlobClient(fileName)).Returns(mockBlobClient.Object);

            // File already exists
            mockBlobClient
                .Setup(b => b.ExistsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(true, new Mock<Response>().Object));

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => _blobStorageService.UploadFileAsync(fileContent, fileName, overwrite: false));
        }

        [Fact]
        public async Task UploadFileAsync_ShouldUploadFileSuccessfully()
        {
            // Arrange
            byte[] fileContent = new byte[] { 1, 2, 3, 4 };
            string fileName = "upload-success.txt";

            var mockBlobClient = new Mock<BlobClient>();
            _mockContainerClient
                .Setup(c => c.GetBlobClient(fileName))
                .Returns(mockBlobClient.Object);

            // File does not exist
            mockBlobClient
                .Setup(b => b.ExistsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(false, new Mock<Response>().Object));

            // Create a fake BlobContentInfo
            var blobContentInfo = BlobsModelFactory.BlobContentInfo(
                new ETag("etag"),
                DateTimeOffset.UtcNow,
                null,
                "versionId",
                "encryptionKey",
                "encryptionScope",
                0
            );

            // Simulate successful upload
            mockBlobClient
                .Setup(b => b.UploadAsync(It.IsAny<Stream>(), true, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(blobContentInfo, new Mock<Response>().Object));

            // Set up the BlobClient.Uri
            var expectedUri = new Uri("https://fakestorage.blob.core.windows.net/container/upload-success.txt");
            mockBlobClient
                .SetupGet(b => b.Uri)
                .Returns(expectedUri);

            // Act
            var result = await _blobStorageService.UploadFileAsync(fileContent, fileName, overwrite: true);

            // Assert
            result.Should().NotBeNull("the service should return a valid Uri after successful upload");
            result.Should().Be(expectedUri);
            mockBlobClient.Verify(
                b => b.UploadAsync(It.IsAny<Stream>(), true, It.IsAny<CancellationToken>()),
                Times.Once
            );
        }

        [Fact]
        public async Task DeleteFileAsync_ShouldThrowArgumentException_WhenFileNameIsNullOrEmpty()
        {
            // Arrange
            string fileName = "";

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => _blobStorageService.DeleteFileAsync(fileName));
        }

        [Fact]
        public async Task DeleteFileAsync_ShouldDeleteFileSuccessfully()
        {
            // Arrange
            string fileName = "delete.txt";

            var mockBlobClient = new Mock<BlobClient>();
            _mockContainerClient.Setup(c => c.GetBlobClient(fileName)).Returns(mockBlobClient.Object);

            // Simulate successful deletion
            mockBlobClient
                .Setup(b => b.DeleteIfExistsAsync(It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(true, new Mock<Response>().Object));

            // Act
            await _blobStorageService.DeleteFileAsync(fileName);

            // Assert
            mockBlobClient.Verify(
                b => b.DeleteIfExistsAsync(It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public void ExistFileAsync_ShouldThrowArgumentException_WhenFileNameIsNullOrEmpty()
        {
            // Arrange
            string fileName = "  ";

            // Act & Assert
            Assert.Throws<ArgumentException>(() => _blobStorageService.ExistFileAsync(fileName));
        }

        [Fact]
        public void ExistFileAsync_ShouldReturnUri_WhenFileExists()
        {
            // Arrange
            string fileName = "exists.txt";
            var expectedUri = new Uri("https://example.com/exists.txt");

            var mockBlobClient = new Mock<BlobClient>();
            _mockContainerClient
                .Setup(c => c.GetBlobClient(fileName))
                .Returns(mockBlobClient.Object);

            var mockResponse = new Mock<Response<bool>>();
            mockResponse.SetupGet(r => r.Value).Returns(true);

            mockBlobClient
                .Setup(b => b.Exists(It.IsAny<CancellationToken>()))
                .Returns(mockResponse.Object);

            mockBlobClient
                .SetupGet(b => b.Uri)
                .Returns(expectedUri);

            // Act
            var result = _blobStorageService.ExistFileAsync(fileName);

            // Assert
            result.Should().Be(expectedUri);
        }

        [Fact]
        public void ExistFileAsync_ShouldReturnNull_WhenFileDoesNotExist()
        {
            // Arrange
            string fileName = "nonexistent.txt";

            var mockBlobClient = new Mock<BlobClient>();
            _mockContainerClient
                .Setup(c => c.GetBlobClient(fileName))
                .Returns(mockBlobClient.Object);

            var mockResponse = new Mock<Response<bool>>();
            mockResponse.SetupGet(r => r.Value).Returns(false);

            mockBlobClient
                .Setup(b => b.Exists(It.IsAny<CancellationToken>()))
                .Returns(mockResponse.Object);

            // Act
            var result = _blobStorageService.ExistFileAsync(fileName);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public async Task DownloadFileByUrlAsync_ShouldThrowFileNotFoundException_WhenBlobDoesNotExist()
        {
            // Arrange
            var blobUrl = new Uri("https://example.com/container/nonexistent.txt");
            string blobName = "nonexistent.txt";

            // Provide a non-null Uri for the mocked container client
            _mockContainerClient
                .SetupGet(c => c.Uri)
                .Returns(new Uri("https://example.com/container"));

            var mockBlobClient = new Mock<BlobClient>();
            _mockContainerClient
                .Setup(c => c.GetBlobClient(blobName))
                .Returns(mockBlobClient.Object);

            // Blob does not exist
            mockBlobClient
                .Setup(b => b.ExistsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(false, new Mock<Response>().Object));

            // Act
            Func<Task> act = async () => await _blobStorageService.DownloadFileByUrlAsync(blobUrl);

            // Assert
            await act.Should().ThrowAsync<FileNotFoundException>()
                .WithMessage($"The blob with the URL {blobUrl} does not exist.");
        }
    }
}
