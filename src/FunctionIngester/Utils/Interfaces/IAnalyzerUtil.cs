using Azure.AI.DocumentIntelligence;
using Entities;
using System;
using System.Collections.Generic;

namespace FunctionIngester.Utils.Interfaces
{
    public interface IAnalyzerUtil
    {
        /// <summary>
        /// Builds a DocumentMap object from the results of Azure AI Document Analysis.
        /// </summary>
        DocumentMap BuildDocumentMapPdf(string myBlobName, string myBlobUri, AnalyzeResult result);
    }
}
