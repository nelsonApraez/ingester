using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Entities;
using FunctionIngester.Interfaces;
using FunctionIngester.Helpers.Interfaces;
using FunctionIngester.Utils.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Integration.Services;

namespace FunctionIngester.Tests
{
    public class DocumentProcessorTests
    {
        private readonly Mock<IDocumentStorage> _mockDocumentStorage;
        private readonly Mock<IDocumentAnalyzer> _mockDocumentAnalyzer;
        private readonly Mock<IDocumentChunker> _mockDocumentChunker;
        private readonly Mock<IDocumentEnricher> _mockDocumentEnricher;
        private readonly Mock<IProcessorUtil> _mockProcessorUtil;
        private readonly Mock<ILogger<DocumentProcessor>> _mockLogger;
        private readonly StorageFolders _storageFolders;
        private readonly DocumentProcessor _documentProcessor;

        public DocumentProcessorTests()
        {
            _mockDocumentStorage = new Mock<IDocumentStorage>();
            _mockDocumentAnalyzer = new Mock<IDocumentAnalyzer>();
            _mockDocumentChunker = new Mock<IDocumentChunker>();
            _mockDocumentEnricher = new Mock<IDocumentEnricher>();
            _mockProcessorUtil = new Mock<IProcessorUtil>();
            _mockLogger = new Mock<ILogger<DocumentProcessor>>();
            _storageFolders = new StorageFolders
            {
                UnprocessedDocs = "unprocessed",
                ProcessedDocs = "processed"
            };

            _documentProcessor = new DocumentProcessor(
                _mockDocumentStorage.Object,
                _mockDocumentAnalyzer.Object,
                _mockDocumentChunker.Object,
                _mockDocumentEnricher.Object,
                _mockProcessorUtil.Object,
                _storageFolders,
                _mockLogger.Object
            );
        }

        /// <summary>
        /// Ensures that InitializeProcessingAsync throws an exception when the file name is null.
        /// </summary>
        [Fact]
        public async Task InitializeProcessingAsync_ShouldThrowArgumentNullException_WhenFileNameIsNull()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                _documentProcessor.InitializeProcessingAsync(null!));
        }

        /// <summary>
        /// Ensures that InitializeProcessingAsync throws an exception when document analysis fails.
        /// </summary>
        [Fact]
        public async Task InitializeProcessingAsync_ShouldThrowApplicationException_WhenAnalysisFails()
        {
            // Arrange
            string fileName = "test.pdf";
            string blobUrl = $"https://storageaccount.blob.core.windows.net/unprocessed/{fileName}";
            var blobData = new BlobData
            {
                Name = fileName,
                Uri = new Uri(blobUrl),
                Content = new BinaryData(new byte[] { 0x1, 0x2, 0x3 }) // Ensure blob is valid
            };

            _mockProcessorUtil.Setup(p => p.ContainerBaseUri).Returns("https://storageaccount.blob.core.windows.net");
            _mockDocumentStorage.Setup(s => s.DownloadFileByUrlAsync(It.IsAny<Uri>())).ReturnsAsync(blobData);
            _mockDocumentAnalyzer.Setup(a => a.AnalyzeDocumentAsync(It.IsAny<BlobData>())).ReturnsAsync((DocumentMap)null!);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ApplicationException>(() =>
                _documentProcessor.InitializeProcessingAsync(fileName));

            Assert.Equal("Document analysis failed. The document could not be processed.", exception.Message);

            // Updated log verification
            _mockLogger.Verify(l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => Convert.ToString(v)!.Contains("Document analysis failed. The document could not be processed.")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()
            ), Times.AtLeastOnce);
        }



        /// <summary>
        /// Ensures that InitializeProcessingAsync processes and uploads chunks successfully.
        /// </summary>
        [Fact]
        public async Task InitializeProcessingAsync_ShouldProcessAndUploadChunksSuccessfully()
        {
            // Arrange
            string fileName = "test.pdf";
            string blobUrl = $"https://storageaccount.blob.core.windows.net/unprocessed/{fileName}";
            var blobData = new BlobData
            {
                Name = fileName,
                Uri = new Uri(blobUrl),
                Content = new BinaryData(new byte[] { 0x1, 0x2, 0x3 }) // Ensure content is valid
            };
            var documentMap = new DocumentMap();
            var chunks = new List<DocumentChunk> { new DocumentChunk { ChunkPath = "path1", JsonContent = "{}" } };
            var enrichedChunks = new List<DocumentChunk> { new DocumentChunk { ChunkPath = "path1", JsonContent = "{\"data\":\"enriched\"}" } };

            _mockProcessorUtil.Setup(p => p.ContainerBaseUri).Returns("https://storageaccount.blob.core.windows.net");
            _mockDocumentStorage.Setup(s => s.DownloadFileByUrlAsync(It.IsAny<Uri>())).ReturnsAsync(blobData);
            _mockDocumentAnalyzer.Setup(a => a.AnalyzeDocumentAsync(It.IsAny<BlobData>())).ReturnsAsync(documentMap);
            _mockDocumentChunker.Setup(c => c.ChunkDocumentAsync(It.IsAny<DocumentMap>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(chunks);
            _mockProcessorUtil.Setup(p => p.AddAdditionalData(It.IsAny<List<DocumentChunk>>(), It.IsAny<string>()))
                .Returns(chunks);
            _mockDocumentEnricher.Setup(e => e.EnrichDocumentAsync(It.IsAny<List<DocumentChunk>>())).ReturnsAsync(enrichedChunks);
            _mockDocumentStorage.Setup(s => s.UploadFilesToStorageAsync(It.IsAny<List<DocumentChunk>>())).Returns(Task.CompletedTask);
            _mockDocumentStorage.Setup(s => s.UploadFilesToAISearch(It.IsAny<List<DocumentChunk>>())).ReturnsAsync(true);
            _mockDocumentStorage.Setup(s => s.MoveBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);

            // Act
            await _documentProcessor.InitializeProcessingAsync(fileName);

            // Assert
            _mockLogger.Verify(l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => Convert.ToString(v)!.Contains("Successfully completed upload of document chunks to Azure Storage and AI Search")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()
            ), Times.Once);


            _mockDocumentStorage.Verify(s => s.UploadFilesToStorageAsync(It.IsAny<List<DocumentChunk>>()), Times.Once);
            _mockDocumentStorage.Verify(s => s.UploadFilesToAISearch(It.IsAny<List<DocumentChunk>>()), Times.Once);
            _mockDocumentStorage.Verify(s => s.MoveBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }


        /// <summary>
        /// Ensures that InitializeProcessingAsync throws an exception when enriched chunks are null or empty.
        /// </summary>
        [Fact]
        public async Task InitializeProcessingAsync_ShouldThrowApplicationException_WhenEnrichedChunksAreEmpty()
        {
            // Arrange
            string fileName = "test.pdf";
            string blobUrl = $"https://storageaccount.blob.core.windows.net/unprocessed/{fileName}";
            var blobData = new BlobData
            {
                Name = fileName,
                Uri = new Uri(blobUrl),
                Content = new BinaryData(new byte[] { 0x1, 0x2, 0x3 }) // Ensure content is not null
            };
            var documentMap = new DocumentMap();
            var chunks = new List<DocumentChunk> { new DocumentChunk { ChunkPath = "path1", JsonContent = "{}" } };

            _mockProcessorUtil.Setup(p => p.ContainerBaseUri).Returns("https://storageaccount.blob.core.windows.net");
            _mockDocumentStorage.Setup(s => s.DownloadFileByUrlAsync(It.IsAny<Uri>())).ReturnsAsync(blobData);
            _mockDocumentAnalyzer.Setup(a => a.AnalyzeDocumentAsync(It.IsAny<BlobData>())).ReturnsAsync(documentMap);
            _mockDocumentChunker.Setup(c => c.ChunkDocumentAsync(It.IsAny<DocumentMap>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(chunks);
            _mockProcessorUtil.Setup(p => p.AddAdditionalData(It.IsAny<List<DocumentChunk>>(), It.IsAny<string>()))
                .Returns(chunks);
            _mockDocumentEnricher.Setup(e => e.EnrichDocumentAsync(It.IsAny<List<DocumentChunk>>()))
                .ReturnsAsync(new List<DocumentChunk>()); // Empty list

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ApplicationException>(() =>
                _documentProcessor.InitializeProcessingAsync(fileName));

            Assert.Equal("The list of enriched chunks is null or empty. Skipping upload to Azure Storage and vectorial database.", exception.Message);

            // log verification
            _mockLogger.Verify(
                l => l.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => Convert.ToString(v)!.Contains("The list of enriched chunks is null or empty. Skipping upload to Azure Storage and vectorial database.")),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ), Times.AtLeastOnce);
        }


    }
}
