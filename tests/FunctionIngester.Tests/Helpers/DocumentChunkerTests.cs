using Moq;
using Microsoft.Extensions.Logging;
using Entities;
using FunctionIngester.Helpers;
using FunctionIngester.Helpers.Interfaces;
using Microsoft.Extensions.Configuration;

namespace FunctionIngester.Tests.Helpers
{
    public class DocumentChunkerTests
    {
        private readonly Mock<ILogger<DocumentChunker>> _mockLogger;
        private readonly StorageFolders _storageFolders;
        private readonly IDocumentChunker _documentChunker;

        public DocumentChunkerTests()
        {
            _mockLogger = new Mock<ILogger<DocumentChunker>>();
            _storageFolders = new StorageFolders
            {
                UnprocessedDocs = "unprocessed",
                ProcessedDocs = "processed",
                ChunkedDocs = "chunked"
            };

            // Create an in-memory configuration with the required value for ChunkTargetSize.
            var inMemorySettings = new Dictionary<string, string>
    {
        {"ConfigurationOpenAI:ChunkTargetSize", "8"}
    };
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            // Now pass the configuration to the DocumentChunker constructor.
            _documentChunker = new DocumentChunker(_storageFolders, configuration, _mockLogger.Object);
        }


        [Fact]
        public void ChunkDocumentAsync_ShouldThrowIfDocumentMapIsNull()
        {
            // Arrange
            DocumentMap documentMap = null!;
            string blobName = "myFile.pdf";
            string blobUri = "https://someuri.com/unprocessed/myFile.pdf";

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                _documentChunker.ChunkDocumentAsync(documentMap, blobName, blobUri));
        }

        [Fact]
        public void ChunkDocumentAsync_ShouldThrowIfBlobNameIsNull()
        {
            // Arrange
            var documentMap = CreateSimpleDocumentMap();
            string blobName = null!;
            string blobUri = "https://someuri.com/unprocessed/myFile.pdf";

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                _documentChunker.ChunkDocumentAsync(documentMap, blobName, blobUri));
        }

        [Fact]
        public void ChunkDocumentAsync_ShouldThrowIfBlobUriIsNull()
        {
            // Arrange
            var documentMap = CreateSimpleDocumentMap();
            string blobName = "myFile.pdf";
            string blobUri = null!;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                _documentChunker.ChunkDocumentAsync(documentMap, blobName, blobUri));
        }

        [Fact]
        public void ChunkDocumentAsync_ShouldReturnMultipleChunksForSmallTargetSize()
        {
            // Arrange
            var documentMap = CreateSampleDocumentMap();
            string blobName = "myFile.pdf";
            string blobUri = "https://someuri.com/unprocessed/myFile.pdf";

            // Act
            var chunks = _documentChunker.ChunkDocumentAsync(documentMap, blobName, blobUri);

            // Assert
            Assert.NotNull(chunks);
            Assert.True(chunks.Count > 1, "Expected more than one chunk due to small chunkTargetSize.");
        }

        [Fact]
        public async Task ChunkDocumentAsync_ShouldCreateSingleChunkForShortText()
        {
            // Arrange
            var documentMap = CreateSimpleDocumentMap();
            string blobName = "shortFile.pdf";
            string blobUri = "https://someuri.com/unprocessed/shortFile.pdf";

            // Act
            var chunks = _documentChunker.ChunkDocumentAsync(documentMap, blobName, blobUri);

            // Assert
            Assert.NotNull(chunks);
            Assert.Single(chunks);
            Assert.Contains("Short text", chunks[0].JsonContent);
            await Task.CompletedTask;
        }

        [Fact]
        public void ChunkDocumentAsync_ShouldSplitLargeParagraph()
        {
            // Arrange
            var documentMap = CreateLargeParagraphDocumentMap();
            string blobName = "largeFile.pdf";
            string blobUri = "https://someuri.com/unprocessed/largeFile.pdf";

            // Act
            var chunks = _documentChunker.ChunkDocumentAsync(documentMap, blobName, blobUri);

            // Assert
            Assert.NotNull(chunks);
            Assert.True(chunks.Count > 1, $"Expected more than one chunk, but got {chunks.Count}");
        }


        [Fact]
        public void ChunkDocumentAsync_ShouldHandleTable()
        {
            // Arrange
            var documentMap = CreateTableDocumentMap();
            string blobName = "tableFile.pdf";
            string blobUri = "https://someuri.com/unprocessed/tableFile.pdf";

            // Act
            var chunks = _documentChunker.ChunkDocumentAsync(documentMap, blobName, blobUri);

            // Assert
            Assert.NotNull(chunks);
            Assert.True(chunks.Count >= 1);
            Assert.Contains("<table>", chunks[0].JsonContent);
        }

        // Verify that an error log is written when an exception occurs
        [Fact]
        public void ChunkDocumentAsync_ShouldLogError_WhenExceptionOccurs()
        {
            // Arrange
            var documentMap = new DocumentMap
            {
                Structure = new List<DocumentStructure>()
            }; // Invalid empty structure

            string blobName = "errorFile.pdf";
            string blobUri = "https://someuri.com/unprocessed/errorFile.pdf";

            // Act
            try
            {
                _documentChunker.ChunkDocumentAsync(documentMap, blobName, blobUri);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception Thrown: {ex.Message}");
            }

            // Assert
            _mockLogger.Verify(l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => Convert.ToString(v)!.Contains("Error chunking document:")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()
            ), Times.Once);
        }


        // Helper methods to create test document maps
        private DocumentMap CreateSimpleDocumentMap() => new DocumentMap
        {
            Structure = new List<DocumentStructure>
            {
                new DocumentStructure
                {
                    Text = "Short text",
                    Section = "Section A",
                    Title = "Title A",
                    Subtitle = "Subtitle A",
                    PageNumber = 1,
                    Type = "text"
                }
            }
        };

        private DocumentMap CreateSampleDocumentMap()
        {
            return new DocumentMap
            {
                Structure = new List<DocumentStructure>
        {
            new DocumentStructure
            {
                Text = "This is a long paragraph designed to exceed the chunkTargetSize threshold. " +
                       "It contains multiple sentences, ensuring that the chunking logic " +
                       "is properly tested when splitting content into multiple sections.",
                Section = "Section A",
                Title = "Title A",
                Subtitle = "Subtitle A",
                PageNumber = 1,
                Type = "text"
            },
            new DocumentStructure
            {
                Text = "Another paragraph follows with additional content. " +
                       "It should also contribute towards the creation of multiple chunks. " +
                       "By increasing the number of words, we force the chunking logic to activate.",
                Section = "Section A",
                Title = "Title A",
                Subtitle = "Subtitle A",
                PageNumber = 2,
                Type = "text"
            }
        }
            };
        }

        private DocumentMap CreateLargeParagraphDocumentMap()
        {
            return new DocumentMap
            {
                Structure = new List<DocumentStructure>
        {
            new DocumentStructure
            {
                Text = string.Join(" ", Enumerable.Repeat(
                    "This is a very long paragraph that should exceed the chunkTargetSize threshold. " +
                    "It contains multiple sentences and many words, ensuring that the chunking logic " +
                    "correctly splits it into multiple sections.",
                    50)),
                Section = "LargeSection",
                Title = "LargeTitle",
                Subtitle = "LargeSubtitle",
                PageNumber = 1,
                Type = "text"
            }
        }
            };
        }


        private DocumentMap CreateTableDocumentMap() => new DocumentMap
        {
            Structure = new List<DocumentStructure>
            {
                new DocumentStructure { Text = "<table><tr><td>Data</td></tr></table>", Section = "Table", Title = "T1", Subtitle = "S1", PageNumber = 2, Type = "table" }
            }
        };
    }
}
