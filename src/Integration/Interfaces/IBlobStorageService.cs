using Integration.Services;

namespace Integration.Interfaces
{  
    /// <summary>
    /// Interface BlobStorage for interacting with Azure Blob Storage.
    /// </summary>
    public interface IBlobStorageService
    {       

        /// <summary>
        /// Moves a blob from one folder to another within the container.
        /// </summary>
        /// <param name="blobName">The name of the blob to move.</param>
        /// <param name="sourceFolder">The source folder path.</param>
        /// <param name="destinationFolder">The destination folder path.</param>
        Task MoveBlobAsync(string blobName, string sourceFolder, string destinationFolder);

        /// <summary>
        /// Uploads a file to the Blob container.
        /// </summary>
        /// <param name="file">The file as a byte array.</param>
        /// <param name="fileName">The name of the file in the container.</param>
        /// <param name="overwrite">Indicates whether to overwrite the file if it already exists.</param>
        /// <param name="WithSas">Indicates whether to generate a SAS URL for the uploaded file.</param>
        /// <returns>The URI of the uploaded file.</returns>
        Task<Uri> UploadFileAsync(byte[] file, string fileName, bool overwrite, bool WithSas = false);

        /// <summary>
        /// Uploads a file to the Blob container.
        /// </summary>
        Task<Uri> UploadJsonContentAsync(string jsonContent, string blobName, bool overwrite);        

        /// <summary>
        /// Downloads a specific file from the Blob container based on its URL.
        /// </summary>
        /// <param name="blobUrl">The URL of the blob to download.</param>
        /// <returns>A BlobData object representing the downloaded file.</returns>
        Task<BlobData> DownloadFileByUrlAsync(Uri blobUrl);

        /// <summary>
        /// Deletes a file from the Blob container.
        /// </summary>
        /// <param name="fileName">The name of the file to delete.</param>
        /// <returns>A task representing the asynchronous delete operation.</returns>
        Task DeleteFileAsync(string fileName);

        /// <summary>
        /// Checks if a file exists in the Blob container and returns its URI.
        /// </summary>
        /// <param name="fileName">The name of the file to check.</param>
        /// <returns>The URI of the file if it exists, or null otherwise.</returns>
        Uri? ExistFileAsync(string fileName);

        string GetContainerName();
    }
}
