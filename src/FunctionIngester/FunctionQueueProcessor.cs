using FunctionIngester.Interfaces;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace FunctionIngester
{
    public class FunctionQueueProcessor
    {
        private readonly IDocumentProcessor _documentProcessor;
        private readonly ILogger<FunctionQueueProcessor> _logger;

        public FunctionQueueProcessor(
            IDocumentProcessor documentProcessor,
            ILogger<FunctionQueueProcessor> logger)
        {
            _documentProcessor = documentProcessor;
            _logger = logger;
        }

        [Function("ProcessDocumentQueue")]
        public async Task RunAsync(
            [QueueTrigger("%AZURESTORAGE_QUEUENAME%", Connection = "AzureWebJobsStorage")] string message,
            FunctionContext context)
        {
            // This log entry confirms that the function was triggered by the queue message
            _logger.LogInformation("Queue trigger function received a message: {message}", message);

            try
            {

                if (string.IsNullOrWhiteSpace(message))
                {
                    _logger.LogWarning("Received an empty message. Skipping processing.");
                    return;
                }


                // Here we assume that 'message' contains the file name or any required data
                var fileName = message.Trim();

                // Call your existing pipeline to process the document
                _logger.LogInformation($"Calling InitializeProcessingAsync for file: {fileName}");
                await _documentProcessor.InitializeProcessingAsync(fileName);


                _logger.LogInformation("Successfully processed file: {fileName}", fileName);
            }
            catch (Exception ex)
            {
                // If an exception is thrown, the queue message may be re-queued depending on the retry settings
                _logger.LogError(ex, "Error processing queue message.");
                throw;
            }
        }
    }
}
