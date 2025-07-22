using Azure.AI.DocumentIntelligence;

namespace Integration.Services
{
    /// <summary>
    /// Interface for the AzureAIService to interact with Azure Text Analytics.
    /// </summary>
    public interface IAzureAIService
    {
        /// <summary>
        /// Extracts key phrases from the provided text.
        /// </summary>
        /// <param name="content">The text to analyze.</param>
        /// <returns>A task representing the asynchronous operation, containing a list of key phrases.</returns>
        Task<List<string>> ExtractKeyPhrasesAsync(string content);

        /// <summary>
        /// Recognizes entities in the provided text.
        /// </summary>
        /// <param name="content">The text to analyze.</param>
        /// <returns>A task representing the asynchronous operation, containing a list of recognized entities.</returns>
        Task<List<string>> ExtractEntitiesAsync(string content);

        /// <summary>
        /// Analyzes a document using the prebuilt-layout model from Document Intelligence.
        /// </summary>
        /// <param name="documentContent">The binary data representing the document to analyze.</param>
        /// <returns>A task representing the asynchronous operation, containing the analysis result.</returns>
        Task<AnalyzeResult> AnalyzeDocumentAsync(BinaryData content);
    }
}
