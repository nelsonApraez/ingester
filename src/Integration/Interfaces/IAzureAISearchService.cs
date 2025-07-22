using Entities;

namespace Integration.Services
{
    public interface IAzureAISearchService
    {
        /// <summary>
        /// Triggers the specified indexer in Azure Search.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation. Returns true if the indexer was triggered successfully.</returns>
        Task RunIndexerAsync();

        /// <summary>
        /// Uploads a list of enriched document chunks directly to the Azure Search index.
        /// </summary>
        /// <param name="enrichedChunks">A list of tuples containing the chunk path and JSON content for each document.</param>
        /// <returns>A task that represents the asynchronous operation. Returns true if the documents were uploaded successfully.</returns>
        Task<bool> UploadFilesAsync(List<DocumentChunk> enrichedChunks);
    }
}
