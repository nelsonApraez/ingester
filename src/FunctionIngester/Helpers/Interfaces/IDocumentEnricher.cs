using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Entities;

namespace FunctionIngester.Helpers.Interfaces
{
    public interface IDocumentEnricher
    {
        Task<List<DocumentChunk>> EnrichDocumentAsync(List<DocumentChunk> chunksWithAdditionalData);
        Task<string?> ExtractOpenAIContextAsync(string content);
        Task<IList<float>> ExtractOpenAIContentVectorAsync(string content);
        Task<string> CleanTextAsync(string content);
    }
}
