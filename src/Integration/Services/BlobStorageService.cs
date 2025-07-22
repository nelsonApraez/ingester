using Azure.Storage.Blobs;
using Integration.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Integration.Services
{
    /// <summary>
    /// Service for interacting with Azure Blob Storage.
    /// Provides functionality to upload, download, check existence, and delete files in a Blob container.
    /// </summary>
    public class BlobStorageService : IBlobStorageService
    {
        private readonly BlobContainerClient _blobContainerClient;
        private readonly ILogger _logger;

        public BlobStorageService(IConfiguration configuration, ILogger<BlobStorageService> logger)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            var secretName = configuration["AzureStorage:ConnectionString:SecretName"];

            var connectionString = !string.IsNullOrWhiteSpace(secretName)
                ? configuration[secretName] ?? configuration["AzureStorage:ConnectionString"]
                : configuration["AzureStorage:ConnectionString"]
                ?? throw new InvalidOperationException("Missing Azure Storage Connection String in configuration");


            var containerName = configuration["AzureStorage:Container"]
                ?? throw new InvalidOperationException("Missing Azure Storage Container in configuration");

            _blobContainerClient = new BlobContainerClient(connectionString, containerName);
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public BlobStorageService(BlobContainerClient blobContainerClient, ILogger<BlobStorageService> logger)
        {
            _blobContainerClient = blobContainerClient ?? throw new ArgumentNullException(nameof(blobContainerClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Ensures that the BlobContainerClient has been initialized before performing any operations.
        /// Throws an exception if the client is null.
        /// </summary>
        private void EnsureInitialized()
        {
            if (_blobContainerClient == null)
            {
                throw new InvalidOperationException("BlobContainerClient has not been initialized. Call Initialize first.");
            }
        }

        /// <summary>
        /// Moves a blob from one folder to another within the same container.
        /// </summary>
        /// <param name="blobName">The name of the blob to move.</param>
        /// <param name="sourceFolder">The folder where the blob currently resides.</param>
        /// <param name="destinationFolder">The folder where the blob will be moved to.</param>
        public async Task MoveBlobAsync(string blobName, string sourceFolder, string destinationFolder)
        {
            try
            {
                EnsureInitialized();

                var sourceBlob = _blobContainerClient.GetBlobClient($"{sourceFolder}/{blobName}");
                var destinationBlob = _blobContainerClient.GetBlobClient($"{destinationFolder}/{blobName}");

                if (await sourceBlob.ExistsAsync())
                {
                    await destinationBlob.StartCopyFromUriAsync(sourceBlob.Uri);
                    await sourceBlob.DeleteAsync();
                }
                else
                {
                    throw new FileNotFoundException($"Source blob '{blobName}' not found in folder '{sourceFolder}'.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error moving blob '{BlobName}': {ErrorMessage}", blobName, ex.Message);
                throw;
            }
        }


        /// <summary>
        /// Uploads a file to the Blob container.
        /// </summary>
        /// <param name="file">The file as a byte array.</param>
        /// <param name="fileName">The name of the file in the container.</param>
        /// <param name="overwrite">Indicates whether to overwrite the file if it already exists.</param>
        /// <param name="WithSas">Indicates whether to generate a SAS URL for the uploaded file.</param>
        /// <returns>The URI of the uploaded file.</returns>
        public async Task<Uri> UploadFileAsync(byte[] file, string fileName, bool overwrite, bool WithSas = false)
        {

            // First, validate parameters in a separate (non-async) method.
            ValidateUploadParameters(file, fileName);

            EnsureInitialized();

            try
            {
                var blobClient = _blobContainerClient.GetBlobClient(fileName);

                if (!overwrite && await blobClient.ExistsAsync())
                {
                    throw new InvalidOperationException($"File '{fileName}' already exists and {nameof(overwrite)} is disabled.");
                }

                using (var stream = new MemoryStream(file))
                {
                    await blobClient.UploadAsync(stream, overwrite);
                }

                return blobClient.Uri;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error uploading file '{FileName}': {ErrorMessage}", fileName, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Deletes a file from the Blob container.
        /// </summary>
        /// <param name="fileName">The name of the file to delete.</param>
        /// <returns>A task representing the asynchronous delete operation.</returns>
        public async Task DeleteFileAsync(string fileName)
        {
            // Validate input parameters synchronously
            ValidateDeleteParameters(fileName);

            // Ensure necessary initialization is done
            EnsureInitialized();

            try
            {
                // Get the blob client for the specified file
                var blobClient = _blobContainerClient.GetBlobClient(fileName);
                await blobClient.DeleteIfExistsAsync();
            }
            catch (Exception ex)
            {
                // Log the error using a static message template with placeholders for structured logging
                _logger.LogError("Error deleting file '{FileName}': {ErrorMessage}", fileName, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Checks if a file exists in the Blob container and returns its URI.
        /// </summary>
        /// <param name="fileName">The name of the file to check.</param>
        /// <returns>The URI of the file if it exists, or null otherwise.</returns>
        public Uri? ExistFileAsync(string fileName)
        {
            EnsureInitialized();

            try
            {
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    throw new ArgumentException("File name cannot be null or empty.", nameof(fileName));
                }

                var blobClient = _blobContainerClient.GetBlobClient(fileName);
                return blobClient.Exists() ? blobClient.Uri : null;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error checking existence of file '{FileName}': {ErrorMessage}", fileName, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Downloads a specific file from the Blob container based on the provided URL.
        /// </summary>
        /// <param name="blobUrl">The URL of the blob to download.</param>
        /// <returns>A BlobData object representing the downloaded file.</returns>
        public async Task<BlobData> DownloadFileByUrlAsync(Uri blobUrl)
        {
            EnsureInitialized();

            try
            {
                // Extract blob name from the URL
                string blobName = GetBlobNameFromUrl(blobUrl);

                // Get a reference to the blob
                var blobClient = _blobContainerClient.GetBlobClient(blobName);

                // Check if the blob exists before attempting to download
                if (await blobClient.ExistsAsync())
                {
                    var response = await blobClient.DownloadContentAsync();
                    return new BlobData
                    {
                        Name = blobClient.Name,
                        Uri = blobClient.Uri,
                        Content = response.Value.Content
                    };
                }
                else
                {
                    throw new FileNotFoundException($"The blob with the URL {blobUrl} does not exist.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error downloading blob by URL '{BlobUrl}': {ErrorMessage}", blobUrl, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Uploads a JSON content to the Blob container.
        /// </summary>
        /// <param name="jsonContent">The JSON content to upload as a string.</param>
        /// <param name="blobName">The name of the blob in the container.</param>
        /// <param name="overwrite">Indicates whether to overwrite the blob if it already exists.</param>
        /// <returns>The URI of the uploaded blob.</returns>
        public async Task<Uri> UploadJsonContentAsync(string jsonContent, string blobName, bool overwrite)
        {
            EnsureInitialized();

            try
            {
                if (string.IsNullOrWhiteSpace(jsonContent))
                {
                    throw new ArgumentException("JSON content cannot be null or empty.", nameof(jsonContent));
                }

                if (string.IsNullOrWhiteSpace(blobName))
                {
                    throw new ArgumentException("Blob name cannot be null or empty.", nameof(blobName));
                }

                // Create a BlobClient for the blob
                var blobClient = _blobContainerClient.GetBlobClient(blobName);

                // Upload the JSON content using BinaryData
                await blobClient.UploadAsync(new BinaryData(jsonContent), overwrite);

                _logger.LogInformation($"Successfully uploaded document chunk to blob: {blobName}");

                // Return the URI of the uploaded blob
                return blobClient.Uri;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error uploading document chunk to blob '{BlobName}': {ErrorMessage}", blobName, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Extracts the blob name from the given blob URL.
        /// </summary>
        /// <param name="blobUrl">The URL of the blob.</param>
        /// <returns>The name of the blob.</returns>
        private string GetBlobNameFromUrl(Uri blobUrl)
        {
            try
            {
                // Assuming the container name is part of the URL, remove it to extract the blob name
                string containerPath = $"{_blobContainerClient.Uri.AbsolutePath}/";
                return blobUrl.AbsolutePath
                    .Replace(containerPath, "", StringComparison.Ordinal)
                    .TrimStart('/');

            }
            catch (Exception ex)
            {
                _logger.LogError("Error extracting blob name from URL '{BlobUrl}': {ErrorMessage}", blobUrl, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Gets the name of the current Blob container.
        /// </summary>
        /// <returns>The name of the Blob container.</returns>
        public string GetContainerName()
        {
            EnsureInitialized();

            return _blobContainerClient.Name;
        }

        /// <summary>
        /// Validates the parameters required to upload a file.
        /// </summary>
        /// <param name="file">The file as a byte array.</param>
        /// <param name="fileName">The name of the file in the container.</param>        
        /// <exception cref="ArgumentException">Thrown if the file or file name is invalid.</exception>
        private static void ValidateUploadParameters(byte[] file, string fileName)
        {
            if (file == null || file.Length == 0)
            {
                throw new ArgumentException("File cannot be null or empty.", nameof(file));
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("File name cannot be null or empty.", nameof(fileName));
            }
        }


        /// <summary>
        /// Validates the parameters required to delete a file.
        /// </summary>
        /// <param name="fileName">The name of the file to delete.</param>
        /// <exception cref="ArgumentException">Thrown if the file name is null, empty, or consists only of whitespace.</exception>
        private static void ValidateDeleteParameters(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("File name cannot be null or empty.", nameof(fileName));
            }
        }

    }

    /// <summary>
    /// Represents a Blob file with its name, URI, and content.
    /// </summary>
    public class BlobData
    {
        public string? Name { get; set; }
        public Uri? Uri { get; set; }
        public BinaryData? Content { get; set; }
    }

}


