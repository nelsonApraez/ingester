using Entities;

namespace FunctionIngester.Utils.Interfaces
{
    public interface IProcessorUtil
    {
        /// <summary>
        /// Adds additional metadata to document chunks.
        /// </summary>
        /// <param name="chunks">The list of document chunks.</param>
        /// <param name="myBlobName">The name of the original blob.</param>
        /// <returns>A list of enriched document chunks.</returns>
        List<DocumentChunk> AddAdditionalData(List<DocumentChunk> chunks, string myBlobName);

        /// <summary>
        /// Gets the base URI of the Azure Blob Storage container.
        /// </summary>
        string ContainerBaseUri { get; }
    }
}
