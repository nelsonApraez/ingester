using Entities;
using Integration.Services;

namespace FunctionIngester.Helpers.Interfaces
{
    public interface IDocumentStorage
    {
        Task<BlobData> DownloadFileByUrlAsync(Uri blobUrl);

        Task UploadFilesToStorageAsync(List<DocumentChunk> enrichedChunksWithAdditionalData);

        Task MoveBlob(string blobName, string sourceFolder, string destinationFolder);

        void RunIndexer();

        Task<bool> UploadFilesToAISearch(List<DocumentChunk> enrichedChunks);
    }
}
