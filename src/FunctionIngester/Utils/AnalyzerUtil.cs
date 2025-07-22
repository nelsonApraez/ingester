using System.Globalization;
using System.Net;
using System.Text;
using Azure.AI.DocumentIntelligence;
using Entities;
using FunctionIngester.Utils.Interfaces;

namespace FunctionIngester.Utils
{
    public class AnalyzerUtil : IAnalyzerUtil
    {
        /// <summary>
        /// Builds a DocumentMap object from the results of Azure AI Document Analysis.
        /// </summary>
        public DocumentMap BuildDocumentMapPdf(string myBlobName, string myBlobUri, AnalyzeResult result)
        {
            // 1) Initialize the DocumentMap with defaults
            var documentMap = InitializeDocumentMap(myBlobName, myBlobUri, result);

            // 2) Mark table spans in the content
            MarkTableContent(documentMap, result);

            // 3) Mark paragraph spans (title, sectionHeading, text) in the content
            MarkParagraphContent(documentMap, result);

            // 4) Collect page numbers for each paragraph start index
            var pageNumberByParagraph = CollectPageNumbers(result);

            // 5) Build the final structured data (titles, sections, text/table content)
            BuildDocumentStructure(documentMap, result, pageNumberByParagraph);

            return documentMap;
        }

        /// <summary>
        /// Initializes a DocumentMap with default values.
        /// </summary>
        private static DocumentMap InitializeDocumentMap(string myBlobName, string myBlobUri, AnalyzeResult result)
        {
            return new DocumentMap
            {
                FileName = myBlobName.Substring(myBlobName.LastIndexOf('/') + 1),
                FileUri = new Uri(myBlobUri, UriKind.Absolute),
                Content = result.Content,
                Structure = new List<DocumentStructure>(),
                ContentType = Enumerable
                    .Repeat(ContentType.NotProcessed, result.Content.Length)
                    .ToList(),
                TableIndex = Enumerable
                    .Repeat(-1, result.Content.Length)
                    .ToList()
            };
        }

        /// <summary>
        /// Marks the content range corresponding to each table in the DocumentMap.
        /// </summary>
        private static void MarkTableContent(DocumentMap documentMap, AnalyzeResult result)
        {
            for (int index = 0; index < result.Tables.Count; index++)
            {
                var table = result.Tables[index];
                // Determine the overall start/end char positions
                int startChar = table.Spans[0].Offset;
                int endChar = startChar + table.Spans[0].Length - 1;

                foreach (var span in table.Spans.Skip(1))
                {
                    int spanStart = span.Offset;
                    startChar = Math.Min(startChar, spanStart);
                    endChar += span.Length - 1;
                }

                // Mark that entire range as table
                MarkTableRange(documentMap, startChar, endChar, index);
            }
        }

        /// <summary>
        /// Marks a specific range in the content as a table, including the TableIndex.
        /// </summary>
        private static void MarkTableRange(DocumentMap documentMap, int startChar, int endChar, int tableIndex)
        {
            documentMap.ContentType[startChar] = ContentType.TableStart;

            // Middle characters
            for (int i = startChar + 1; i < endChar; i++)
            {
                documentMap.ContentType[i] = ContentType.TableChar;
            }

            // End character
            documentMap.ContentType[endChar] = ContentType.TableEnd;
            documentMap.TableIndex[endChar] = tableIndex;
        }

        /// <summary>
        /// Marks the content range corresponding to each paragraph (title, sectionHeading, or text).
        /// </summary>
        private static void MarkParagraphContent(DocumentMap documentMap, AnalyzeResult result)
        {
            foreach (var paragraph in result.Paragraphs)
            {
                // Each paragraph has at least one span
                int startChar = paragraph.Spans[0].Offset;
                int endChar = startChar + paragraph.Spans[0].Length - 1;

                // Only mark if it is still NotProcessed
                if (documentMap.ContentType[startChar] == ContentType.NotProcessed)
                {
                    if (paragraph.Role == null)
                    {
                        // Mark as text
                        MarkParagraphRange(documentMap, startChar, endChar,
                            ContentType.TextStart, ContentType.TextChar, ContentType.TextEnd);
                    }
                    else
                    {
                        var roleString = paragraph.Role.ToString();
                        if (roleString == "title")
                        {
                            MarkParagraphRange(documentMap, startChar, endChar,
                                ContentType.TitleStart, ContentType.TitleChar, ContentType.TitleEnd);
                        }
                        else if (roleString == "sectionHeading")
                        {
                            MarkParagraphRange(documentMap, startChar, endChar,
                                ContentType.SectionHeadingStart, ContentType.SectionHeadingChar, ContentType.SectionHeadingEnd);
                        }
                    }
                }

            }
        }

        /// <summary>
        /// Marks a specific range in the content as a paragraph of the given types (start/char/end).
        /// </summary>
        private static void MarkParagraphRange(
            DocumentMap documentMap,
            int startChar,
            int endChar,
            ContentType startType,
            ContentType charType,
            ContentType endType)
        {
            documentMap.ContentType[startChar] = startType;
            for (int i = startChar + 1; i < endChar; i++)
            {
                documentMap.ContentType[i] = charType;
            }
            documentMap.ContentType[endChar] = endType;
        }

        /// <summary>
        /// Creates a dictionary mapping the startChar of each paragraph to its page number.
        /// </summary>
        private static Dictionary<int, int> CollectPageNumbers(AnalyzeResult result)
        {
            var pageNumberByParagraph = new Dictionary<int, int>();
            foreach (var paragraph in result.Paragraphs)
            {
                int startChar = paragraph.Spans[0].Offset;
                // Assume the first bounding region always exists
                pageNumberByParagraph[startChar] = paragraph.BoundingRegions[0].PageNumber;
            }
            return pageNumberByParagraph;
        }

        /// <summary>
        /// Iterates over each character in the DocumentMap, detecting "start" and "end" content types
        /// and building up the structured paragraphs/tables with titles and sections.
        /// </summary>
        private void BuildDocumentStructure(
            DocumentMap documentMap,
            AnalyzeResult result,
            Dictionary<int, int> pageNumberByParagraph)
        {
            string mainTitle = string.Empty;
            string currentTitle = string.Empty;
            string currentSection = string.Empty;
            int startPosition = 0;
            int pageNumber = 0;

            for (int index = 0; index < documentMap.ContentType.Count; index++)
            {
                var item = documentMap.ContentType[index];

                // Update page number if we have a paragraph start here
                if (pageNumberByParagraph.TryGetValue(index, out int foundPage))
                {
                    pageNumber = foundPage;
                }

                if (IsStartType(item))
                {
                    // Just record the start position
                    startPosition = index;
                }
                else if (IsEndType(item))
                {
                    // Process the "end" scenario
                    ProcessEndType(
                        documentMap,
                        result,
                        item,
                        ref mainTitle,
                        ref currentTitle,
                        ref currentSection,
                        ref pageNumber,
                        startPosition,
                        index);
                }
            }
        }

        /// <summary>
        /// Determines if the content type is one of the "start" markers.
        /// </summary>
        private static bool IsStartType(ContentType item)
        {
            return item == ContentType.TitleStart
                || item == ContentType.SectionHeadingStart
                || item == ContentType.TextStart
                || item == ContentType.TableStart;
        }

        /// <summary>
        /// Determines if the content type is one of the "end" markers.
        /// </summary>
        private static bool IsEndType(ContentType item)
        {
            return item == ContentType.TitleEnd
                || item == ContentType.SectionHeadingEnd
                || item == ContentType.TextEnd
                || item == ContentType.TableEnd;
        }

        /// <summary>
        /// Handles the logic for "end" content types (TitleEnd, SectionHeadingEnd, TextEnd, TableEnd).
        /// </summary>
        private static void ProcessEndType(
            DocumentMap documentMap,
            AnalyzeResult result,
            ContentType item,
            ref string mainTitle,
            ref string currentTitle,
            ref string currentSection,
            ref int pageNumber,
            int startPosition,
            int endPosition)
        {
            switch (item)
            {
                case ContentType.TitleEnd:
                    HandleTitleEnd(documentMap, ref mainTitle, ref currentTitle, pageNumber, startPosition, endPosition);
                    break;

                case ContentType.SectionHeadingEnd:
                    currentSection = ExtractSubstring(documentMap, startPosition, endPosition);
                    break;

                case ContentType.TextEnd:
                case ContentType.TableEnd:
                    HandleTextOrTableEnd(documentMap, result, item, mainTitle, currentTitle, currentSection, pageNumber, startPosition, endPosition);
                    break;
            }
        }

        /// <summary>
        /// Sets the current title and updates the mainTitle if needed.
        /// </summary>
        private static void HandleTitleEnd(
            DocumentMap documentMap,
            ref string mainTitle,
            ref string currentTitle,
            int pageNumber,
            int startPosition,
            int endPosition)
        {
            currentTitle = ExtractSubstring(documentMap, startPosition, endPosition);

            // If we haven't set the main title yet or we are on the first page, update mainTitle
            if (string.IsNullOrEmpty(mainTitle) || pageNumber == 1)
            {
                mainTitle = string.IsNullOrEmpty(mainTitle)
                    ? currentTitle
                    : $"{mainTitle}; {currentTitle}";
            }
        }

        /// <summary>
        /// Handles finalizing either text or table content into the DocumentStructure list.
        /// </summary>
        private static void HandleTextOrTableEnd(
            DocumentMap documentMap,
            AnalyzeResult result,
            ContentType item,
            string mainTitle,
            string currentTitle,
            string currentSection,
            int pageNumber,
            int startPosition,
            int endPosition)
        {
            string outputText = (item == ContentType.TextEnd)
                ? ExtractSubstring(documentMap, startPosition, endPosition)
                : TableToHtml(result.Tables[documentMap.TableIndex[endPosition]]);

            documentMap.Structure.Add(new DocumentStructure
            {
                Offset = startPosition,
                Text = outputText,
                Type = (item == ContentType.TextEnd) ? "text" : "table",
                Title = mainTitle,
                Subtitle = currentTitle,
                Section = currentSection,
                PageNumber = pageNumber
            });
        }

        /// <summary>
        /// Extracts a substring from the DocumentMap's main content using start and end positions (inclusive).
        /// </summary>
        private static string ExtractSubstring(DocumentMap documentMap, int startPosition, int endPosition)
        {
            return documentMap.Content.Substring(startPosition, endPosition - startPosition + 1);
        }

        /// <summary>
        /// Converts a DocumentTable object to an HTML representation.
        /// </summary>
        private static string TableToHtml(DocumentTable table)
        {
            var tableHtml = new StringBuilder("<table>");
            foreach (var rowGroup in table.Cells.GroupBy(c => c.RowIndex).OrderBy(g => g.Key))
            {
                tableHtml.Append("<tr>");
                foreach (var cell in rowGroup.OrderBy(c => c.ColumnIndex))
                {
                    var tag = (cell.Kind == "columnHeader" || cell.Kind == "rowHeader") ? "th" : "td";
                    var cellSpans = string.Empty;
                    if (cell.ColumnSpan > 1)
                    {
                        cellSpans += $" colSpan='{cell.ColumnSpan}'";
                    }

                    if (cell.RowSpan > 1)
                    {
                        cellSpans += $" rowSpan='{cell.RowSpan}'";
                    }

                    tableHtml.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "<{0}{1}>{2}</{0}>",
                        tag,
                        cellSpans,
                        WebUtility.HtmlEncode(cell.Content));
                }
                tableHtml.Append("</tr>");
            }

            tableHtml.Append("</table>");
            return tableHtml.ToString();
        }
    }
}
