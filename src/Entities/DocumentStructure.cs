namespace Entities
{
    public class DocumentStructure
    {
        public int Offset { get; set; }
        public string Text { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        public int PageNumber { get; set; }
    }
}
