namespace Entities
{
    public class DocumentChunk
    {
        /// <summary>
        /// Path or identifier of the generated chunk.
        /// </summary>
        public string ChunkPath { get; set; } = null!;

        /// <summary>
        /// JSON content of the chunk.
        /// </summary>
        public string JsonContent { get; set; } = null!;
    }
}
