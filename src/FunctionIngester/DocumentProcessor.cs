using Entities;
using FunctionIngester.Helpers.Interfaces;
using FunctionIngester.Interfaces;
using FunctionIngester.Utils.Interfaces;
using Integration.Services;
using Microsoft.Extensions.Logging;

namespace FunctionIngester
{
    public class DocumentProcessor : IDocumentProcessor
    {
        private readonly ILogger<DocumentProcessor> _logger;
        private readonly IDocumentStorage _documentStorage;
        private readonly IDocumentAnalyzer _documentAnalyzer;
        private readonly IDocumentChunker _documentChunker;
        private readonly IDocumentEnricher _documentEnricher;
        private readonly IProcessorUtil _processorUtil;
        private readonly StorageFolders _storageFolders;

        public DocumentProcessor(IDocumentStorage documentStorage,
            IDocumentAnalyzer documentAnalyzer,
            IDocumentChunker documentChunker,
            IDocumentEnricher documentEnricher,
            IProcessorUtil processorUtil,
            StorageFolders storageFolders,
            ILogger<DocumentProcessor> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _documentStorage = documentStorage;
            _documentAnalyzer = documentAnalyzer;
            _documentChunker = documentChunker;
            _documentEnricher = documentEnricher;
            _storageFolders = storageFolders;
            _processorUtil = processorUtil;
        }

        /// <summary>
        /// Initializes the document processing workflow.
        /// Downloads a file from Azure Blob Storage, validates and analyzes it, chunks the document,
        /// enriches the chunks, adds additional data, and uploads the processed chunks to Azure Storage 
        /// and the vectorial database in parallel.
        /// </summary>
        /// <param name="blobUrl">The URL of the blob to process.</param>        
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task InitializeProcessingAsync(string fileName)
        {
            _logger.LogInformation("Starting document processing...");

            // Validate input parameters
            ArgumentNullException.ThrowIfNull(fileName, nameof(fileName));

            // Get Container base URI
            var baseUri = _processorUtil.ContainerBaseUri;

            var blobUrl = new Uri($"{baseUri}/{_storageFolders.UnprocessedDocs}/{fileName}");

            try
            {
                // Step 1: Download the blob from Azure Storage
                var blobData = await _documentStorage.DownloadFileByUrlAsync(blobUrl).ConfigureAwait(false);                

                // Step 2: Validate the blob data and content
                if (!ValidateBlob(blobData))
                {
                    const string errorMsg = $"Blob validation failed. The document is empty or invalid.";
                    _logger.LogWarning(errorMsg);                    
                    throw new InvalidOperationException(errorMsg);
                }

                // Step 3: Analyze the blob and extract document structure
                var documentMap = await _documentAnalyzer.AnalyzeDocumentAsync(blobData);                

                if (documentMap == null)
                {
                    const string errorMsg = "Document analysis failed. The document could not be processed.";
                    _logger.LogError(errorMsg);
                    throw new ApplicationException(errorMsg);
                }

                // Step 4: Perform chunking on the document
                var Chunks = _documentChunker.ChunkDocumentAsync(
                    documentMap,
                    blobData.Name!,
                    blobData.Uri!.ToString()
                );

                // Step 5: Add additional metadata to the enriched chunks
                var chunksWithAdditionalData = _processorUtil.AddAdditionalData(Chunks, blobData.Name!);

                // Step 6: Enrich the document chunks
                var enrichedChunks = await _documentEnricher.EnrichDocumentAsync(chunksWithAdditionalData);

                if (enrichedChunks == null || enrichedChunks.Count == 0)
                {
                    const string errorMsg = "The list of enriched chunks is null or empty. Skipping upload to Azure Storage and vectorial database.";
                    _logger.LogError(errorMsg);
                    throw new ApplicationException(errorMsg);
                }

                // Step 7: Upload to Azure Storage and vectorial database in parallel
                var uploadToStorageTask = _documentStorage.UploadFilesToStorageAsync(enrichedChunks);
                var uploadToAISearchTask = _documentStorage.UploadFilesToAISearch(enrichedChunks);

                await Task.WhenAll(uploadToStorageTask, uploadToAISearchTask);

                _logger.LogInformation("Successfully completed upload of document chunks to Azure Storage and AI Search");

                // Step 8: Move the processed document to the "processed" folder
                await _documentStorage.MoveBlob(blobData.Name!, _storageFolders.UnprocessedDocs, _storageFolders.ProcessedDocs).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred during document processing: {ex.Message}");
                throw;
            }

        }


        /// <summary>
        /// Validates if the blob data and content are valid for processing.
        /// </summary>
        private bool ValidateBlob(BlobData blobData)
        {
            if (blobData == null || blobData.Content == null)
            {
                _logger.LogWarning($"Invalid blob data or content. Skipping...");
                return false;
            }

            return true;
        }


    }
}
