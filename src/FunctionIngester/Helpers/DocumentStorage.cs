using Integration.Interfaces;
using Integration.Services;
using Microsoft.Extensions.Logging;
using Entities;
using FunctionIngester.Helpers.Interfaces;

namespace FunctionIngester.Helpers
{
    public class DocumentStorage : IDocumentStorage
    {
        private readonly IBlobStorageService _blobStorageService;
        private readonly IAzureAISearchService _azureAISearchService;
        private readonly ILogger<DocumentStorage> _logger;

        public DocumentStorage(IBlobStorageService blobStorageService, IAzureAISearchService azureAISearchService, ILogger<DocumentStorage> logger)
        {
            _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
            _azureAISearchService = azureAISearchService ?? throw new ArgumentNullException(nameof(azureAISearchService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }       

        public async Task<BlobData> DownloadFileByUrlAsync(Uri blobUrl)
        {
            try
            {
                var result = await _blobStorageService.DownloadFileByUrlAsync(blobUrl);
                return result;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error downloading the blob: {blobUrl}", ex);
            }
        }

        public async Task UploadFilesToStorageAsync(List<DocumentChunk> enrichedChunksWithAdditionalData)
        {
            try
            {
                var containerName = _blobStorageService.GetContainerName();
                _logger.LogInformation($"Uploading JSON content to container: {containerName}");

                // Iterate through each chunk in the list
                foreach (var chunk in enrichedChunksWithAdditionalData)
                {

                    string chunkPath = chunk.ChunkPath;
                    string jsonContent = chunk.JsonContent;

                    if (string.IsNullOrWhiteSpace(chunkPath))
                    {
                        _logger.LogWarning("Chunk path is null or empty. Skipping this chunk.");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(jsonContent))
                    {
                        _logger.LogWarning($"JSON content for chunk path '{chunkPath}' is null or empty. Skipping this chunk.");
                        continue;
                    }

                    _logger.LogInformation($"Uploading chunk to path: {chunkPath}");

                    // Upload the JSON content using the BlobStorageService
                    await _blobStorageService.UploadJsonContentAsync(jsonContent, chunkPath, true);

                    _logger.LogInformation($"Successfully uploaded chunk to path: {chunkPath}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error uploading JSON content to Blob Storage: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Moves a blob from the source folder to the destination folder.
        /// </summary>
        public async Task MoveBlob(string blobName, string sourceFolder, string destinationFolder)
        {
            try
            {
                await _blobStorageService.MoveBlobAsync(blobName, sourceFolder, destinationFolder);
                _logger.LogInformation($"Moved blob {blobName} from {sourceFolder} to {destinationFolder}");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException
                              and not StackOverflowException
                              and not ThreadAbortException)
            {
                // Log unexpected, non-critical exceptions
                _logger.LogError($"Failed to move blob {blobName}: {ex.Message}");
                throw;
            }
        }

        public void RunIndexer()
        {
            _azureAISearchService.RunIndexerAsync();
        }

        public async Task<bool> UploadFilesToAISearch(List<DocumentChunk> enrichedChunks)
        {
            try
            {
                // Validate if the list has elements
                if (enrichedChunks == null || enrichedChunks.Count == 0)
                {
                    _logger.LogWarning("The list of enriched chunks is null or empty. Skipping upload to the vectorial database.");
                    return false;
                }

                _logger.LogInformation("Uploading enriched chunks to the vectorial database...");

                // Delegate the upload process to the Azure AI Search service
                var success = await _azureAISearchService.UploadFilesAsync(enrichedChunks);

                if (success)
                {
                    _logger.LogInformation("Successfully uploaded all enriched chunks to the vectorial database.");
                }
                else
                {
                    _logger.LogWarning("Failed to upload some or all chunks to the vectorial database.");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error uploading enriched chunks to the vectorial database: {ex.Message}");
                throw;
            }
        }
    }
}
