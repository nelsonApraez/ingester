namespace FunctionIngester.Interfaces
{
    public interface IDocumentProcessor
    {
        /// <summary>
        /// Initializes the document processing workflow.
        /// </summary>
        /// <param name="fileName">The name of the file to process.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task InitializeProcessingAsync(string fileName);
    }
}
