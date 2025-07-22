# Azure Function-Based Document Processing Solution

This repository contains an Azure Function solution for processing and analyzing documents using Azure AI Services. The system processes PDF documents stored in Azure Blob Storage, analyzes their structure and content using Azure Document Intelligence, and divides them into smaller, manageable chunks for further processing, storage, embedding, and indexing.

## Features

- **Document Analysis:** Uses Azure Document Intelligence to analyze document content and extract structured information.
- **Chunking:** Splits documents into smaller parts (chunks) based on size, structure, or content type (e.g., paragraphs, sections, or tables).
- **Enrichment:** Enhances chunks with metadata such as:
  - **Key Phrases** extracted using Azure AI Language.
  - **Entities** identified within the content.
  - **Context** to provide additional insights into the chunk.
- **Embedding Generation:** Creates vectorized embeddings of each chunk using Azure OpenAI’s `text-embedding-ada-002` model.
- **Azure Blob Storage Integration:** Processes documents from Azure Blob Storage container folder (`unprocessedDocs`) and stores enriched chunks back in the appropriate container folder (`chunkedDocs`).
- **Azure AI Search Integration:** Uploads enriched and embedded chunks to Azure AI Search for efficient indexing and retrieval.

## Components

### 1. **Azure Functions**
   - **Event Grid Trigger Function:** Automatically triggered when a new PDF document is uploaded to the `unprocessedDocs` folder inside an Azure Blob Storage container.
   - Extracts the document's filename from the Event Grid event payload.
   - Initiates the document processing pipeline.

### 2. **DocumentAnalyzer**
   - Interacts with **Azure Document Intelligence** to analyze document content.
   - Extracts key insights such as text, structure, and layout.

### 3. **DocumentChunker**
   - Processes the extracted content and **chunks** the document into smaller, structured segments.
   - Organizes content into meaningful paragraphs and sections.
   - Converts each chunk into a structured **JSON object**.

### 4. **Enrichment**
   - Enhances each chunk with metadata using **Azure AI Services**:
     - **Key Phrases**: Identifies important concepts and terms.
     - **Entities**: Extracts named entities (e.g., organizations, dates, people).
     - **Context**: Adds semantic meaning to the chunks.
     - **Vectorized Content (Embeddings)**: Generates embeddings for AI-powered search and retrieval.

### 5. **Storage**
   - Manages data persistence using **Azure Blob Storage** and **Azure AI Search**.
   - Stores enriched chunks in Azure Blob Storage (`chunkedDocs` folder inside `containerDocs`).
   - Uploads vectorized content to **Azure AI Search** for indexing and retrieval.
   - Moves processed documents from `unprocessedDocs` to `processedDocs` after successful completion.


## How It Works

1. **Document Ingestion (Event-Driven)**
   - A PDF document is uploaded to **Azure Blob Storage** in `containerDocs/unprocessedDocs`.
   - **Event Grid** detects the new blob and triggers the **Azure Function** automatically.
   - The function extracts the document's filename from the Event Grid event payload.
   - The function calls `InitializeProcessingAsync` to start the document processing pipeline.

2. **Document Analysis**
   - The function invokes the **DocumentAnalyzer** helper to analyze the document using **Azure Document Intelligence**.
   - The extracted content includes **text, structure, and layout**.

3. **Chunking and Enrichment**
   - The **DocumentChunker** processes the extracted content, splitting it into structured **chunks**.
   - Each chunk is enriched with:
     - **Key phrases** (semantic highlights)
     - **Entities** (e.g., dates, names, organizations)
     - **Context** (relevant metadata)
     - **Vector embeddings** for AI-driven search and retrieval.

4. **Storage and Indexing**
   - Enriched chunks are stored in **Azure Blob Storage** under `containerDocs/chunkedDocs`.
   - The enriched content is also uploaded to **Azure AI Search** for indexing and intelligent retrieval.
   - Once successfully processed, the original document is **moved from `unprocessedDocs` to `processedDocs`** for tracking.


## Prerequisites

- **Azure Account:** An active Azure subscription.
- **Azure Resources:**
  - Azure Blob Storage.
  - Azure AI Services Multi-Service Account (Document Intelligence and AI Language for Text Analytics).
  - Azure AI Search for indexing enriched chunks.
  - Azure OpenAI Service for generating content context and embeddings.
  - Azure Key Vault for secure secret management.
- **Development Environment:**
  - .NET 8 SDK.
  - Visual Studio or Visual Studio Code.

## Getting Started

Follow these steps to deploy and configure the function in Azure:

### 1. Clone the Repository

```sh
git clone https://github.com/nelsonApraez/ingester.git
cd your-repository-name
```

### 2. Install Dependencies

Ensure you have the **.NET 8 SDK** installed. Then, restore dependencies:

```sh
dotnet restore
```

### 3. Set Up Azure Resources

Before deploying the application, you need to configure the required **Azure resources**:

1. **Azure Blob Storage**:
   - Create a **storage account** with a container named `container-docs`.
   - Inside `container-docs`, create a **folder named `unprocessed-docs`** to store incoming PDF documents.
   - Ensure document names follow a **normalized naming convention**.

2. **Azure Event Grid**:
   - Set up an **Event Grid Subscription** to trigger the function when a file is uploaded to `unprocessed-docs`.
   - Use the **"Storage Account (Blob Created)"** event type to notify the function when a new document is uploaded.
   - The event subscription should target the **HTTP-triggered Azure Function endpoint**.

3. **Azure AI Services**:
   - Create an **AI Services Multi-Service Account** to use:
     - **Document Intelligence** for document analysis.
     - **AI Language (Text Analytics)** for key phrase and entity extraction.
   
4. **Azure OpenAI Service**:
   - Set up the **embedding model** `text-embedding-ada-002` for vector representation of document content.
   - Deploy the **GPT-4o model** to provide additional context to document chunks, enhancing their relevance for AI-driven search and retrieval.

5. **Azure AI Search**:
   - Create an **index** to store enriched document chunks.
   - Configure the **index schema** to accommodate extracted metadata, key phrases, entities, and vector embeddings.
   - Ensure the index supports **vector search** for efficient retrieval based on semantic similarity.

6. **Azure Key Vault**:
   - Store necessary API keys and connection strings securely.

### 4. Environment Variables and Secrets Configuration

For the Azure Function to operate correctly, certain **environment variables** must be set in the Function App configuration, and corresponding **secrets** must be securely stored in **Azure Key Vault**.

#### 1. Environment Variables

These environment variables should be configured in the **Azure Function App Settings**:

| Variable Name                          | Description |
|----------------------------------------|-------------|
| `AZURE_STORAGE_CONTAINER`              | The name of the Azure Blob Storage container where documents are stored. |
| `AZURE_STORAGE_FOLDERS_CHUNKED_DOCS`   | Folder inside the storage container where processed document chunks are stored. |
| `AZURE_STORAGE_FOLDERS_FAILED_PROCESSING_DOCS` | Folder inside the storage container for documents that failed processing. |
| `AZURE_STORAGE_FOLDERS_UNPROCESSED_DOCS` | Folder inside the storage container where new (unprocessed) documents are uploaded. |
| `AZURE_STORAGE_FOLDERS_PROCESSED_DOCS`  | Folder inside the storage container where successfully processed documents are moved. |
| `AZURE_AI_SEARCH_ENDPOINT`              | The endpoint URL for the **Azure AI Search** instance. |
| `AZURE_AI_SEARCH_INDEX_NAME`            | The index name configured in **Azure AI Search** for storing enriched document chunks. |
| `AZURE_AI_ENDPOINT`                      | The endpoint URL for the **Azure AI Services Multi-Service Account**. |
| `OPENAI_ENDPOINT`                        | The endpoint URL for the **Azure OpenAI Service**. |
| `OPENAI_MODEL`                           | The **GPT-4o model** used to provide additional context to document chunks. |
| `OPENAI_EMBEDDING_MODEL`                 | The **text-embedding-ada-002** model used to generate vector embeddings. |
| `OPENAI_MAX_TOKENS`                      | Maximum number of tokens for OpenAI responses. e.g. `100`  |
| `OPENAI_TEMPERATURE`                     | Controls randomness in OpenAI responses (range: `0.0` - `1.0`). Recommended value: `0.3`|
| `OPENAI_PRESENCE_PENALTY`                | Adjusts bias towards introducing new concepts in OpenAI responses. e.g. `0`|
| `OPENAI_FREQUENCY_PENALTY`               | Adjusts bias towards reducing repetition in OpenAI responses. e.g. `0` |
| `OPENAI_TOPP`                            | Nucleus sampling parameter for OpenAI responses. e.g. `0.6` |
| `CHUNK_TARGET_SIZE`                      | Target size for document chunking (in number of characters). Recommended value: `750` |
| `KEYVAULT_URI`                           | The **Azure Key Vault** URI where API keys and secrets are securely stored. |

#### 2. Azure Key Vault Secrets

These secrets must be stored in **Azure Key Vault** and referenced in the function app settings:

| Secret Name                                           | Description |
|------------------------------------------------------|-------------|
| `dev-kvs-project-genai-aisearch-primary-key`        | Primary key for **Azure AI Search**. |
| `dev-kvs-project-genai-aisearch-secondary-key`      | Secondary key for **Azure AI Search**. |
| `dev-kvs-project-genai-aiservices-primary-key`      | Primary key for **Azure AI Services** (Document Intelligence and AI Language). |
| `dev-kvs-project-genai-aiservices-secondary-key`    | Secondary key for **Azure AI Services**. |
| `dev-kvs-project-genai-openai-primary-key`          | Primary API key for **Azure OpenAI Service**. |
| `dev-kvs-project-genai-openai-secondary-key`        | Secondary API key for **Azure OpenAI Service**. |
| `dev-kvs-project-genai-st-connection-string`        | Connection string for **Azure Blob Storage**. |

#### 3. Configuring Environment Variables in Azure Portal

1. Go to **Azure Portal** → **Function App**.
2. Navigate to **Configuration** → **Application Settings**.
3. Click **+ New application setting**, enter the **variable name** and **value**.
4. Click **Save**.

#### 4. Storing Secrets in Azure Key Vault

1. Go to **Azure Portal** → **Key Vaults**.
2. Open your **Key Vault** and navigate to **Secrets**.
3. Click **+ Generate/Import** and enter the **secret name** and **value**.
4. Save the secret and copy the **Secret Identifier (URI)**.
5. In the **Function App Configuration**, use the following format to reference a Key Vault secret:
   ```sh
   @Microsoft.KeyVault(SecretUri=https://your-keyvault-name.vault.azure.net/secrets/your-secret-name)
   ```
6. Save the configuration and restart the **Function App** to apply changes.

By configuring these environment variables and secrets correctly, the function will securely integrate with **Azure AI Services, Azure AI Search, Azure OpenAI, and Blob Storage** for automated document processing.

### 5. Role Privileges

This section outlines the necessary **role-based access control (RBAC) permissions** required for the **Azure Function** and **developers** to interact securely with different Azure services. Proper role assignments ensure that the function operates with the **least privilege principle**, while developers maintain the necessary access for development and debugging.

#### 1. **Azure Blob Storage**
   - **Function Role:** `Storage Blob Data Contributor`
     - Grants read/write access to blobs for document ingestion and storage of processed data.
   - **Developer Role:** `Storage Blob Data Contributor`, `EventGrid Contributor`
     - `Storage Blob Data Contributor`: Allows developers to test uploading and retrieving documents.
     - `EventGrid Contributor`: Required to configure and manage **Event Grid** triggers.

#### 2. **Azure Key Vault**
   - **Function Role:** `Key Vault Secrets User`
     - Grants permission to **retrieve secrets** such as API keys, storage connection strings, and authentication tokens.
   - **Developer Role:** `Key Vault Secrets Officer`
     - Allows developers to **manage secrets** in **Azure Key Vault** for testing and debugging.

#### 3. **Azure AI Search**
   - **Function Role:** *(No specific role required)*
     - The function interacts with **Azure AI Search** via an API key.
   - **Developer Role:** `Search Index Data Reader`, `Search Service Contributor`
     - `Search Index Data Reader`: Allows querying indexed documents.
     - `Search Service Contributor`: Enables creating and modifying search indexes.

#### 4. **Azure OpenAI**
   - **Function Role:** *(No specific role required)*
     - The function accesses **OpenAI API** using an API key.
   - **Developer Role:** `Cognitive Services User`
     - Allows developers to interact with **OpenAI models** for testing and development.

#### 5. **Azure AI Services Multi-Service Account**
   - **Function Role:** *(No specific role required)*
     - The function accesses AI services via API keys and endpoints.
   - **Developer Role:** `Cognitive Services User`
     - Grants access to AI-powered services, such as **Azure Document Intelligence**.

#### 6. **Azure Function**
   - **Function Role:** *(No additional role required)*
     - The function operates with the permissions assigned to interact with other Azure resources.
   - **Developer Role:** `Reader`
     - Grants **read-only access** to function configurations for debugging and monitoring.


These permissions are designed to ensure that the **Azure Function** has the required access for seamless execution while following best security practices. **API keys and connection strings** are used for authentication where necessary, avoiding excessive role assignments.

### 6. Deploy the Azure Function

You can deploy the Azure Function using different methods:

#### **Option 1: Azure Functions Core Tools**
Use the following command to deploy directly from your local machine:

```sh
func azure functionapp publish <your-function-app-name>
```

#### **Option 2: Azure CLI**

Alternatively, you can deploy the function using the **Azure CLI**:

```sh
az functionapp deployment source config-zip \
  --resource-group <your-resource-group> \
  --name <your-function-app-name> \
  --src <path-to-your-zip-file>
```

#### **Option 3: Visual Studio**

If you're using **Visual Studio**, follow these steps:

1. Open the **Azure Functions** project in **Visual Studio**.
2. Right-click on the **project** in **Solution Explorer** and select **Publish**.
3. Choose **Azure** as the target and select **Azure Function App (Windows/Linux)**.
4. Sign in to your **Azure account** and select the correct **Subscription**.
5. Choose an existing **Function App** or create a new one.
6. Click **Next**, review the settings, and click **Finish**.
7. Finally, click **Publish** to deploy the function.

Once deployed, verify that the function is running by checking the **Azure Portal** or using:

```sh
az functionapp show --name <your-function-app-name> --resource-group <your-resource-group>
```

### 7. Configure Event Grid Subscription

After deploying the function, create an **Event Grid Subscription** to ensure it is triggered when a new file is uploaded:

```sh
az eventgrid event-subscription create \
  --name <your-subscription-name> \
  --source-resource-id <your-storage-account-id> \
  --endpoint "https://<your-function-app-name>.azurewebsites.net/api/ProcessDocument?code=<your-function-key>" \
  --event-delivery-schema eventgrid \
  --included-event-types Microsoft.Storage.BlobCreated
```

### 8. Test the Solution

To test the deployment:

- Upload a **PDF file** to the `unprocessed-docs` folder in **Azure Blob Storage**.
- Event Grid should trigger the function automatically.
- Check the **Azure Monitor Logs** to confirm document processing.

If you need to simulate an Event Grid event manually, use the **Azure CLI**:

```sh
az eventgrid event-publish --topic-endpoint <your-event-grid-topic-endpoint> \
  --subject "/blobServices/default/containers/container-docs/blobs/unprocessed-docs/sample-document.pdf" \
  --event-type "Microsoft.Storage.BlobCreated" \
  --data "{}" --event-time "$(date -u +"%Y-%m-%dT%H:%M:%SZ")" --content-type application/json
```

This ensures that your function is correctly set up and integrated with Azure Event Grid for automated document processing.


## Contributing

Contributions are welcome! Please submit a pull request or open an issue for discussion.

