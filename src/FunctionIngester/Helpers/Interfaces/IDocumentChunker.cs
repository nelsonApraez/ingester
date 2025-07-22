using Entities;

namespace FunctionIngester.Helpers.Interfaces
{
    public interface IDocumentChunker
    {
        List<DocumentChunk> ChunkDocumentAsync(DocumentMap documentMap, string myBlobName, string myBlobUri);
    }
}
