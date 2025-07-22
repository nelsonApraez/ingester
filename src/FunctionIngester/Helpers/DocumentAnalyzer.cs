using Entities;
using FunctionIngester.Helpers.Interfaces;
using FunctionIngester.Utils;
using FunctionIngester.Utils.Interfaces;
using Integration.Services;
using Microsoft.Extensions.Logging;

namespace FunctionIngester.Helpers
{
    public class DocumentAnalyzer : IDocumentAnalyzer
    {
        private readonly IAzureAIService _azureAIService;
        private readonly ILogger<DocumentAnalyzer> _logger;
        private readonly IAnalyzerUtil _documentUtils;

        public DocumentAnalyzer(IAzureAIService azureAIService, IAnalyzerUtil documentUtils, ILogger<DocumentAnalyzer> logger)
        {
            _azureAIService = azureAIService ?? throw new ArgumentNullException(nameof(azureAIService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _documentUtils = documentUtils ?? throw new ArgumentNullException(nameof(documentUtils));
        }

        public async Task<DocumentMap?> AnalyzeDocumentAsync(BlobData blobData)
        {
            _logger.LogInformation($"Analyzing Document: {blobData.Name}");

            if (string.IsNullOrEmpty(blobData.Name) || blobData.Content == null || blobData.Uri == null)
            {
                throw new ArgumentException("Blob name cannot be null or empty, blob content cannot be null, and blob URI cannot be null.", nameof(blobData));
            }

            try
            {
                blobData.Name = GetBlobFileName(blobData.Name);

                var analysisResult = await _azureAIService.AnalyzeDocumentAsync(blobData.Content).ConfigureAwait(false);
                if (analysisResult == null)
                {
                    _logger.LogWarning($"Analysis result is null for {blobData.Name}.");
                    // await MoveBlob(blobData.Name, storageFolders.UnprocessedDocs, storageFolders.FailedProcessingDocs);
                    return null;
                }

                var documentMap = _documentUtils.BuildDocumentMapPdf(blobData.Name, blobData.Uri.ToString(), analysisResult);

                if (documentMap == null)
                {
                    _logger.LogWarning($"Document map is null for {blobData.Name}.");
                    // await MoveBlob(blobData.Name, storageFolders.UnprocessedDocs, storageFolders.FailedProcessingDocs);
                    return null;
                }

                return documentMap;

            }
            catch (Exception ex)
            {

                _logger.LogError($"Error analyzing blob {blobData.Name}: {ex.Message}.");
                // await MoveBlob(blobData.Name, storageFolders.UnprocessedDocs, storageFolders.FailedProcessingDocs);
                return null;
            }
        }

        /// <summary>
        /// Extracts the file name from the blob path.
        /// </summary>
        private static string GetBlobFileName(string blobPath)
        {
            return blobPath!.Substring(blobPath.LastIndexOf('/') + 1);
        }
    }
}
