using Entities;
using Integration.Services;

namespace FunctionIngester.Helpers.Interfaces
{
    public interface IDocumentAnalyzer
    {
        Task<DocumentMap?> AnalyzeDocumentAsync(BlobData blobData);
    }
}
