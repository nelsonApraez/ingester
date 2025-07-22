using OpenAI.Chat;

namespace Integration.Interfaces
{
    public interface IOpenAIService
    {
        /// <summary>
        /// Creates a chat completion response in plain text format based on the provided list of chat messages.
        /// </summary>
        /// <param name="requests">The list of chat messages to be sent to the model.</param>
        /// <param name="modelFormat">The format of the model's response. Defaults to "text".</param>
        /// <returns>A task representing the asynchronous operation, with a string result containing the model's response.</returns>
        Task<string> CreateChatCompletionTextAsync(IList<ChatMessage> requests, string modelFormat = "text");

        /// <summary>
        /// Creates a chat completion response as a full object, including metadata and additional information.
        /// </summary>
        /// <param name="requests">The list of chat messages to be sent to the model.</param>
        /// <param name="modelFormat">The format of the model's response. Defaults to "text".</param>
        /// <returns>A task representing the asynchronous operation, with a ChatCompletion object containing the model's response.</returns>
        Task<ChatCompletion> CreateChatCompletionAsync(IList<ChatMessage> requests, string modelFormat = "text");

        /// <summary>
        /// Generates an embedding vector for a given input string using the specified embedding model.
        /// </summary>
        /// <param name="input">The input text to generate the embedding for.</param>
        /// <returns>A task representing the asynchronous operation, with a list of floats representing the embedding vector.</returns>
        Task<IList<float>?> GetEmbeddingAsync(string input);
    }
}
