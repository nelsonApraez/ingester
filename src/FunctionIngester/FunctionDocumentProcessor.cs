using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Messaging.EventGrid;
using Azure.Storage.Queues;
using FunctionIngester.Interfaces;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Resolvers;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace FunctionIngester
{
    public class FunctionDocumentProcessor
    { 
        public FunctionDocumentProcessor()
        {            
        }

        /// <summary>
        /// Processes a PDF document from an Event Grid event and enqueues the file name
        /// for further asynchronous processing.
        /// </summary>
        /// <param name="req">The HTTP request triggering the function (Event Grid).</param>
        /// <param name="executionContext">The FunctionContext for logging and other info.</param>
        /// <returns>HTTP response indicating success or failure.</returns>
        [Function("ProcessDocument")]
        [OpenApiOperation(
            operationId: "ProcessDocument",
            tags: new[] { "DocumentProcessing" },
            Summary = "Processes a PDF document for AI-driven analysis (Event Grid -> Queue)",
            Description = "This function is triggered by an **Event Grid event** when a PDF document is uploaded to a designated container in Azure Blob Storage. Instead of processing directly, it enqueues the file name for asynchronous processing by a separate Queue-triggered function.\n\n" +
                          "**Trigger Source:**\n" +
                          "- The function is triggered when a **new file is uploaded** to the `unprocessedDocs` folder within a configured container in Azure Blob Storage.\n" +
                          "- The event payload contains metadata about the uploaded document, including its storage location and name.\n\n" +
                          "**Processing Steps:**\n" +
                          "1. **Receives an Event Grid event** from Azure Storage when a document is uploaded.\n" +
                          "2. **Extracts the file name** from the event payload.\n" +
                          "3. **Validates** the document name to ensure it is a PDF and meets naming requirements.\n" +
                          "4. **Enqueues** the file name to a Storage Queue for offline processing.\n" +
                          "5. **Returns** a quick success response to Event Grid (avoiding timeouts).\n" +
                          "6. A **separate QueueTrigger function** performs the heavy processing.\n\n" +
                          "**Failure Handling & Retries:**\n" +
                          "- If something fails during the offline processing, the queue message may be re-tried.\n" +
                          "- Event Grid receives an immediate success, so it will not re-trigger due to timeouts.",
            Visibility = OpenApiVisibilityType.Important)]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.OK,
            contentType: "application/json",
            bodyType: typeof(string),
            Summary = "Enqueue successful",
            Description = "Returns a success message upon completion of the enqueuing process.")]
        [OpenApiResponseWithoutBody(
            statusCode: HttpStatusCode.BadRequest,
            Summary = "Invalid input",
            Description = "The input event payload is invalid. This may occur if:\n" +
                          "- The event subject does not contain a valid file path.\n" +
                          "- The file is **not a PDF**.\n" +
                          "- The document name is missing or incorrectly formatted.")]
        [OpenApiResponseWithoutBody(
            statusCode: HttpStatusCode.InternalServerError,
            Summary = "Enqueue failed",
            Description = "An error occurred while enqueuing the document for processing. The response contains details of the failure.")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequestData req,
            FunctionContext executionContext)
        {
            var logger = executionContext.GetLogger("ProcessDocument");
            logger.LogInformation("Event Grid HTTP trigger received.");

            try
            {
                // 1) Parse request body and events
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var parseResult = await ParseEventGridEvents(requestBody, logger, req);
                if (parseResult.Response != null)
                {
                    // Early return if no valid events
                    return parseResult.Response;
                }

                // 2) Handle subscription validation if present
                var subscriptionResponse = await HandleSubscriptionValidation(requestBody, parseResult.Events, logger, req);
                if (subscriptionResponse != null)
                {
                    return subscriptionResponse;
                }

                // 3) Process blob-created events (enqueuing the file name)
                var processingErrorResponse = await ProcessBlobCreatedEventsAsync(parseResult.Events, logger, req);
                if (processingErrorResponse != null)
                {
                    return processingErrorResponse;
                }

                // 4) Return success response
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteStringAsync("Document enqueued for asynchronous processing.");
                return response;
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning($"Validation error during document processing: {ex.Message}");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Validation failed", ex.Message);
            }
            catch (ApplicationException ex)
            {
                logger.LogError($"Processing error: {ex.Message}");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Enqueue failed", ex.Message);
            }
            catch (JsonException ex)
            {
                logger.LogError($"JSON parsing error: {ex.Message}");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Invalid JSON", ex.Message);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException || ex is StackOverflowException || ex is ThreadAbortException))
            {
                // Log unexpected non-critical exceptions and return an error response.
                logger.LogError($"Unexpected error during enqueue operation: {ex.Message}");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Unexpected error", ex.Message);
            }
        }

        /// <summary>
        /// Attempts to parse the incoming JSON into a list of EventGridEvent objects.
        /// Returns a tuple where Response is non-null if there's an error (e.g., no valid events).
        /// </summary>
        private static async Task<(List<EventGridEvent> Events, HttpResponseData? Response)> ParseEventGridEvents(
            string requestBody,
            ILogger logger,
            HttpRequestData req)
        {
            var eventGridEvents = JsonSerializer.Deserialize<List<EventGridEvent>>(requestBody);

            if (eventGridEvents == null || eventGridEvents.Count == 0)
            {
                logger.LogWarning("No valid Event Grid events received.");

                var errorResponse = await CreateErrorResponse(
                    req,
                    HttpStatusCode.BadRequest,
                    "Validation failed",
                    "No events found in request."
                );

                return (new List<EventGridEvent>(), errorResponse);
            }

            return (eventGridEvents, null);
        }

        /// <summary>
        /// Checks if any of the events are subscription validation events. If found, returns a response; otherwise null.
        /// </summary>
        private static async Task<HttpResponseData?> HandleSubscriptionValidation(
            string requestBody,
            IEnumerable<EventGridEvent> eventGridEvents,
            ILogger logger,
            HttpRequestData req)
        {
            var subscriptionValidationEvents = eventGridEvents
                .Where(e => e.EventType == "Microsoft.EventGrid.SubscriptionValidationEvent")
                .ToList();

            if (subscriptionValidationEvents.Count == 0)
            {
                return null;
            }

            using var jsonDoc = JsonDocument.Parse(requestBody);

            foreach (var element in jsonDoc.RootElement.EnumerateArray())
            {
                if (!element.TryGetProperty("data", out var dataElement))
                {
                    continue;
                }

                if (!dataElement.TryGetProperty("validationCode", out var validationCodeElement))
                {
                    continue;
                }

                string validationCode = validationCodeElement.GetString()!;
                logger.LogInformation(
                    $"Event Grid validation request received. Responding with validationCode: {validationCode}"
                );

                var validationResponse = req.CreateResponse(HttpStatusCode.OK);
                await validationResponse.WriteStringAsync($"{{\"validationResponse\": \"{validationCode}\"}}");

                return validationResponse;
            }

            return null;
        }

        /// <summary>
        /// Processes all events that are "Microsoft.Storage.BlobCreated". Instead of doing heavy processing,
        /// it enqueues the file name for asynchronous processing by the QueueTrigger function.
        /// </summary>
        private async Task<HttpResponseData?> ProcessBlobCreatedEventsAsync(
            List<EventGridEvent> eventGridEvents,
            ILogger logger,
            HttpRequestData req)
        {
            foreach (var eventGridEvent in eventGridEvents)
            {
                if (eventGridEvent.EventType != "Microsoft.Storage.BlobCreated")
                {
                    logger.LogWarning($"Skipping event: {eventGridEvent.EventType}");
                    continue;
                }

                string subject = eventGridEvent.Subject;
                logger.LogInformation($"Received Event Grid event with subject: {subject}");

                string fileName = Path.GetFileName(subject);

                if (string.IsNullOrWhiteSpace(fileName))
                {
                    logger.LogWarning("Validation error: Invalid or missing 'fileName' parameter.");
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Validation failed", "Invalid or missing 'fileName' parameter.");
                }

                // Validate the file name (lowercase, PDF, etc.)
                ValidateFileName(fileName);

                logger.LogInformation($"Enqueuing blob: {fileName} for offline processing...");

                // Enqueue the file name instead of processing it here
                await EnqueueMessageAsync(fileName, logger);
            }

            // No error encountered
            return null;
        }

        /// <summary>
        /// Validates whether the given file name follows normalization rules.
        /// </summary>
        private static bool IsNormalizedFileName(string fileName)
        {
            return fileName == fileName.ToLowerInvariant()
                && !fileName.Contains(' ')
                && !(fileName.StartsWith('.') || fileName.EndsWith('.'))
                && fileName.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-');
        }

        /// <summary>
        /// Validates the file name (must be PDF, normalized, etc.).
        /// </summary>
        private static void ValidateFileName(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new InvalidOperationException("Invalid or missing 'fileName' parameter.");
            }

            if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Invalid file format. Only PDF documents are supported.");
            }

            if (!IsNormalizedFileName(fileName))
            {
                throw new InvalidOperationException("Invalid file name. Document name must be normalized (lowercase, no spaces, no special characters).");
            }
        }

        /// <summary>
        /// Creates an error response.
        /// </summary>
        private static async Task<HttpResponseData> CreateErrorResponse(
            HttpRequestData req,
            HttpStatusCode statusCode,
            string error,
            string details)
        {
            var response = req.CreateResponse(statusCode);
            response.Headers.Add("Content-Type", "application/json");

            var errorMessage = new { error, details };
            await response.WriteStringAsync(JsonSerializer.Serialize(errorMessage));

            return response;
        }

        /// <summary>
        /// Enqueues the file name to a Storage Queue for offline processing.
        /// </summary>
        private async Task EnqueueMessageAsync(string fileName, ILogger logger)
        {
            // Retrieve the queue name from environment variables or default
            string queueName = Environment.GetEnvironmentVariable("AZURESTORAGE_QUEUENAME") ?? "documents-to-process";

            // Retrieve the AzureWebJobsStorage connection string
            string? connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("AzureWebJobsStorage is not configured.");
            }

            // Create the queue client
            var queueClient = new QueueClient(connectionString, queueName);

            // Create the queue if it does not exist
            await queueClient.CreateIfNotExistsAsync();

            // Encode filename to base64 for queue message
            string base64Message = Convert.ToBase64String(Encoding.UTF8.GetBytes(fileName));

            // Send the fileName as a queue message
            await queueClient.SendMessageAsync(base64Message);

            logger.LogInformation($"Successfully enqueued message for file: {fileName}");
        }

        // Example class for Swagger (optional)
        public class FileNameExample : OpenApiExample<string>
        {
            public override IOpenApiExample<string> Build(Newtonsoft.Json.Serialization.NamingStrategy? namingStrategy = null)
            {
                this.Examples.Add(OpenApiExampleResolver.Resolve(
                    "ExampleFileName",
                    "sample-document.pdf"
                ));
                return this;
            }
        }
    }
}
