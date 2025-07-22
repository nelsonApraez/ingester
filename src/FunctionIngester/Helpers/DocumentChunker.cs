using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Azure.Storage.Blobs;
using Entities;
using FunctionIngester.Helpers.Interfaces;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FunctionIngester.Helpers
{
    public class DocumentChunker : IDocumentChunker
    {
        private readonly ILogger<DocumentChunker> _logger;
        private readonly int _chunkTargetSize;
        private string _previousTableHeader = "";
        private readonly StorageFolders _storageFolders;

        public DocumentChunker(StorageFolders storageFolders, IConfiguration configuration, ILogger<DocumentChunker> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _storageFolders = storageFolders ?? throw new ArgumentNullException(nameof(storageFolders));

            var chunkSizeValue = configuration["ConfigurationOpenAI:ChunkTargetSize"]
                ?? throw new InvalidOperationException("Missing ConfigurationOpenAI:ChunkTargetSize in configuration.");

            if (!int.TryParse(chunkSizeValue, out _chunkTargetSize))
            {
                throw new InvalidOperationException("Invalid value for ConfigurationOpenAI:ChunkTargetSize.");
            }
        }


        /// <summary>
        /// Splits the document into smaller chunks based on the specified chunk size,
        /// sections, titles, and subtitles. It also handles large paragraphs separately
        /// by calling ProcessLargeParagraph if needed.
        /// </summary>
        /// <param name="documentMap">Document map containing paragraphs and metadata.</param>
        /// <param name="myBlobName">Name of the blob file.</param>
        /// <param name="myBlobUri">URI of the original blob.</param>
        /// <returns>A list of enriched chunks.</returns>
        public List<DocumentChunk> ChunkDocumentAsync(
            DocumentMap documentMap,
            string myBlobName,
            string myBlobUri)
        {
            _logger.LogInformation("Chunking document...");

            try
            {

                // Validate input parameters
                ValidateChunkParameters(documentMap, myBlobName, myBlobUri);

                // Initialize local variables
                string chunkText = string.Empty;
                int chunkSize = 0;
                int fileNumber = 0;
                var chunks = new List<DocumentChunk>();
                var pageList = new List<int>();

                // Initialize "previous" metadata based on the first paragraph
                string previousSectionName = documentMap.Structure[0].Section;
                string previousTitleName = documentMap.Structure[0].Title;
                string previousSubtitleName = documentMap.Structure[0].Subtitle;

                // Iterate over each paragraph
                foreach (var paragraphElement in documentMap.Structure.Select((value, index) => new { value, index }))
                {
                    // Skip paragraph if its text is null or whitespace
                    if (string.IsNullOrWhiteSpace(paragraphElement.value.Text))
                    {
                        _logger.LogWarning($"Skipping paragraph at index {paragraphElement.index} because its content is empty or null.");
                        continue;
                    }

                    var paragraphSize = TokenCount(paragraphElement.value.Text);
                    var paragraphText = paragraphElement.value.Text;
                    var sectionName = paragraphElement.value.Section;
                    var titleName = paragraphElement.value.Title;
                    var subtitleName = paragraphElement.value.Subtitle;

                    // Determine if we need to close the current chunk before processing this paragraph
                    bool needsNewChunk = ShouldStartNewChunk(
                        chunkSize,
                        paragraphSize,
                        sectionName, previousSectionName,
                        titleName, previousTitleName,
                        subtitleName, previousSubtitleName
                    );

                    // If a new chunk is needed, write the current chunk (if not empty) and reset
                    if (needsNewChunk)
                    {
                        WriteAndResetChunkIfNotEmpty(
                            ref chunkText,
                            ref chunkSize,
                            ref fileNumber,
                            pageList,
                            previousSectionName,
                            previousTitleName,
                            previousSubtitleName,
                            myBlobName,
                            myBlobUri,
                            chunks
                        );
                    }

                    // If the paragraph is larger than the chunk size, handle it separately
                    if (paragraphSize >= _chunkTargetSize)
                    {
                        ProcessLargeParagraph(
                            _chunkTargetSize,
                            paragraphSize,
                            paragraphText,
                            myBlobName,
                            myBlobUri,
                            ref fileNumber,
                            pageList,
                            sectionName,
                            titleName,
                            subtitleName,
                            paragraphElement.value.Type,
                            _storageFolders,
                            chunks
                        );

                        // Skip adding this paragraph text again
                        continue;
                    }
                    else
                    {
                        // Accumulate this paragraph into the current chunk
                        AccumulateParagraph(
                            paragraphElement.value,
                            ref chunkText,
                            ref chunkSize,
                            pageList
                        );
                    }

                    // If this is the last paragraph, write the final chunk
                    if (paragraphElement.index == documentMap.Structure.Count - 1)
                    {
                        WriteAndResetChunkIfNotEmpty(
                            ref chunkText,
                            ref chunkSize,
                            ref fileNumber,
                            pageList,
                            sectionName,
                            titleName,
                            subtitleName,
                            myBlobName,
                            myBlobUri,
                            chunks
                        );
                    }

                    // Update "previous" metadata
                    previousSectionName = sectionName;
                    previousTitleName = titleName;
                    previousSubtitleName = subtitleName;
                }

                return chunks;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error chunking document: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Writes a chunk to a JSON string and returns the (chunkPath, jsonContent) tuple.
        /// </summary>
        private DocumentChunk WriteChunk(
            string myBlobName,
            string myBlobUri,
            StorageFolders storageFolders,
            string fileNumber,
            int chunkSize,
            string chunkText,
            List<int> pageList,
            string sectionName,
            string titleName,
            string subtitleName)
        {
            // Build the JSON object
            var chunkOutput = new
            {
                file_name = myBlobName,
                file_uri = ReplaceFolderInUrl(myBlobUri, storageFolders.UnprocessedDocs, storageFolders.ProcessedDocs),
                processed_datetime = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                chunk_file = $"{myBlobName}-{fileNumber}.json",
                file_class = "text",
                folder = storageFolders.ChunkedDocs,
                title = titleName,
                subtitle = subtitleName,
                section = sectionName,
                pages = pageList,
                token_count = chunkSize,
                content = chunkText
            };

            _logger.LogInformation($"Writing chunk {chunkOutput.chunk_file}...");
            string jsonStr = JsonSerializer.Serialize(chunkOutput, SerializerOptions);

            string chunkPath = $"{storageFolders.ChunkedDocs}/{myBlobName}-{fileNumber}.json";

            return new DocumentChunk
            {
                ChunkPath = chunkPath,
                JsonContent = jsonStr
            };

        }

        /// <summary>
        /// Processes a large paragraph that exceeds the chunk size.
        /// If it is a table, delegates to ProcessTableParagraph.
        /// Otherwise, delegates to ProcessTextParagraph.
        /// </summary>
        private void ProcessLargeParagraph(
            int chunkTargetSize,
            int paragraphSize,
            string paragraphText,
            string myBlobName,
            string myBlobUri,
            ref int fileNumber,
            List<int> pageList,
            string sectionName,
            string titleName,
            string subtitleName,
            string paragraphType,
            StorageFolders storageFolders,
            List<DocumentChunk> enrichedChunks)
        {
            if (paragraphType == "table")
            {
                ProcessTableParagraph(
                    chunkTargetSize,
                    paragraphText,
                    myBlobName,
                    myBlobUri,
                    ref fileNumber,
                    pageList,
                    sectionName,
                    titleName,
                    subtitleName,
                    storageFolders,
                    enrichedChunks
                );
            }
            else
            {
                ProcessTextParagraph(
                    chunkTargetSize,
                    paragraphSize,
                    paragraphText,
                    myBlobName,
                    myBlobUri,
                    ref fileNumber,
                    pageList,
                    sectionName,
                    titleName,
                    subtitleName,
                    storageFolders,
                    enrichedChunks
                );
            }
        }

        /// <summary>
        /// Processes a paragraph that is a table, splitting it into chunks with headers.
        /// </summary>
        private void ProcessTableParagraph(
            int chunkTargetSize,
            string paragraphText,
            string myBlobName,
            string myBlobUri,
            ref int fileNumber,
            List<int> pageList,
            string sectionName,
            string titleName,
            string subtitleName,
            StorageFolders storageFolders,
            List<DocumentChunk> enrichedChunks)
        {
            var tableChunks = ChunkTableWithHeaders("", paragraphText, chunkTargetSize);

            for (int i = 0; i < tableChunks.Count; i++)
            {
                string tableChunk = tableChunks[i];
                int tokens = TokenCount(tableChunk);

                var enrichedChunk = WriteChunk(
                    myBlobName,
                    myBlobUri,
                    storageFolders,
                    $"{fileNumber}-{i}",
                    tokens,
                    tableChunk,
                    pageList,
                    sectionName,
                    titleName,
                    subtitleName
                );

                enrichedChunks.Add(enrichedChunk);
            }

            fileNumber++;
        }

        /// <summary>
        /// Processes a large text paragraph (not a table), splitting it into sub-chunks by sentences.
        /// </summary>
        private void ProcessTextParagraph(
            int chunkTargetSize,
            int paragraphSize,
            string paragraphText,
            string myBlobName,
            string myBlobUri,
            ref int fileNumber,
            List<int> pageList,
            string sectionName,
            string titleName,
            string subtitleName,
            StorageFolders storageFolders,
            List<DocumentChunk> enrichedChunks)
        {
            // Split by sentences
            var sentences = paragraphText.Split(new[] { ". " }, StringSplitOptions.RemoveEmptyEntries);
            var subChunks = new List<string>();
            var currentChunk = string.Empty;

            foreach (var sentence in sentences)
            {
                var tempChunk = string.IsNullOrEmpty(currentChunk) ? sentence : $"{currentChunk} {sentence}";

                if (TokenCount(tempChunk) <= chunkTargetSize)
                {
                    currentChunk = tempChunk;
                    continue;
                }

                if (!string.IsNullOrEmpty(currentChunk))
                {
                    subChunks.Add(currentChunk);
                }
                currentChunk = sentence;
            }

            if (!string.IsNullOrEmpty(currentChunk))
            {
                subChunks.Add(currentChunk);
            }

            // Write each sub-chunk
            for (int i = 0; i < subChunks.Count; i++)
            {
                string textChunk = subChunks[i];
                int tokens = TokenCount(textChunk);

                var enrichedChunk = WriteChunk(
                    myBlobName,
                    myBlobUri,
                    storageFolders,
                    $"{fileNumber}-{i}",
                    tokens,
                    textChunk,
                    pageList,
                    sectionName,
                    titleName,
                    subtitleName
                );

                enrichedChunks.Add(enrichedChunk);
            }

            fileNumber++;
        }

        /// <summary>
        /// Counts the number of tokens (words) in the input text.
        /// </summary>
        private static int TokenCount(string inputText)
        {
            if (string.IsNullOrWhiteSpace(inputText))
            {
                return 0;
            }

            return inputText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        }

        /// <summary>
        /// Splits a table into smaller chunks, adding headers if needed.
        /// </summary>
        private List<string> ChunkTableWithHeaders(string prefixText, string tableHtml, int standardChunkTargetSize)
        {
            var chunks = new List<string>();
            // Use a StringBuilder to avoid string concatenation in a loop
            var currentChunkBuilder = new StringBuilder(prefixText);
            int chunkTargetSize = standardChunkTargetSize - TokenCount(prefixText);

            var doc = new HtmlDocument();
            doc.LoadHtml(tableHtml);

            // Extract table headers (if present)
            var theadNode = doc.DocumentNode.SelectSingleNode("//thead");
            string thead = theadNode != null ? theadNode.OuterHtml : string.Empty;

            // If this is a continuation of a previous table, add the stored header
            if (!string.IsNullOrEmpty(_previousTableHeader))
            {
                thead = !string.IsNullOrEmpty(thead)
                    ? thead.Replace("<thead>", $"<thead>{_previousTableHeader}", StringComparison.Ordinal)
                    : $"<thead>{_previousTableHeader}</thead>";
            }

            var rows = doc.DocumentNode.SelectNodes("//tr") ?? new HtmlNodeCollection(null);

            foreach (var row in rows)
            {
                string rowHtml = row.OuterHtml;

                // Check if adding rowHtml would exceed the chunk target size
                if (TokenCount(currentChunkBuilder.ToString() + rowHtml) > chunkTargetSize)
                {
                    if (currentChunkBuilder.Length > 0)
                    {
                        chunks.Add($"{currentChunkBuilder.ToString()}</table>");
                    }

                    // Start a new chunk with the header
                    currentChunkBuilder.Clear();
                    currentChunkBuilder.Append($"<table>{thead}");
                    chunkTargetSize = standardChunkTargetSize;
                }

                // Append rowHtml using StringBuilder
                currentChunkBuilder.Append(rowHtml);
            }

            // Add the last chunk
            if (currentChunkBuilder.Length > 0)
            {
                chunks.Add($"{currentChunkBuilder.ToString()}</table>");
            }

            _previousTableHeader = thead?.Replace("<thead>", "", StringComparison.Ordinal)
                             .Replace("</thead>", "", StringComparison.Ordinal) ?? string.Empty;


            return chunks;
        }

        /// <summary>
        /// Replaces a folder in the given URL with another folder.
        /// </summary>
        private static string ReplaceFolderInUrl(string url, string oldFolder, string newFolder)
        {
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(oldFolder) || string.IsNullOrEmpty(newFolder))
            {
                throw new ArgumentException("The URL or folder names cannot be empty.");
            }

            return url.Replace($"/{oldFolder}/", $"/{newFolder}/", StringComparison.Ordinal);

        }        

        /// <summary>
        /// Validates the required parameters for the chunking process.
        /// </summary>
        private void ValidateChunkParameters(DocumentMap documentMap, string myBlobName, string myBlobUri)
        {
            ArgumentNullException.ThrowIfNull(documentMap);
            ArgumentNullException.ThrowIfNull(myBlobName);
            ArgumentNullException.ThrowIfNull(myBlobUri);
            ArgumentNullException.ThrowIfNull(_chunkTargetSize);

            // Optionally, check if documentMap.Structure is empty to avoid index errors
            if (documentMap.Structure == null || documentMap.Structure.Count == 0)
            {
                throw new InvalidOperationException("Document map structure is empty or null.");
            }
        }

        /// <summary>
        /// Determines if a new chunk should start based on the current and previous metadata.
        /// </summary>
        private bool ShouldStartNewChunk(
            int currentChunkSize,
            int newParagraphSize,
            string sectionName, string previousSectionName,
            string titleName, string previousTitleName,
            string subtitleName, string previousSubtitleName)
        {
            bool sizeExceeded = (currentChunkSize + newParagraphSize >= _chunkTargetSize);
            bool differentSection = (sectionName != previousSectionName);
            bool differentTitle = (titleName != previousTitleName);
            bool differentSubtitle = (subtitleName != previousSubtitleName);

            return sizeExceeded || differentSection || differentTitle || differentSubtitle;
        }

        /// <summary>
        /// Writes the current chunk if chunkText is not empty, then resets accumulators.
        /// </summary>
        private void WriteAndResetChunkIfNotEmpty(
            ref string chunkText,
            ref int chunkSize,
            ref int fileNumber,
            List<int> pageList,
            string sectionName,
            string titleName,
            string subtitleName,
            string myBlobName,
            string myBlobUri,
            List<DocumentChunk> chunks)
        {
            if (!string.IsNullOrWhiteSpace(chunkText))
            {
                var finishedChunk = WriteChunk(
                    myBlobName,
                    myBlobUri,
                    _storageFolders,
                    fileNumber.ToString(CultureInfo.InvariantCulture),
                    chunkSize,
                    chunkText,
                    pageList,
                    sectionName,
                    titleName,
                    subtitleName
                );

                chunks.Add(finishedChunk);
                fileNumber++;

                // Reset accumulators
                pageList.Clear();
                chunkText = string.Empty;
                chunkSize = 0;
            }
        }

        /// <summary>
        /// Accumulates the paragraph text and updates the chunk size/pages.
        /// </summary>
        private static void AccumulateParagraph(
            DocumentStructure paragraph,
            ref string chunkText,
            ref int chunkSize,
            List<int> pageList)
        {
            if (paragraph.PageNumber != 0 && !pageList.Contains(paragraph.PageNumber))
            {
                pageList.Add(paragraph.PageNumber);
            }

            int paragraphSize = TokenCount(paragraph.Text);
            chunkSize += paragraphSize;

            if (!string.IsNullOrEmpty(chunkText))
            {
                chunkText += "\n" + paragraph.Text;
            }
            else
            {
                chunkText = paragraph.Text;
            }
        }

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }
}
