namespace Entities
{
    public class DocumentMap
    {
        public string FileName { get; set; } = string.Empty;
        public Uri FileUri { get; set; } = new Uri("about:blank");
        public string Content { get; set; } = string.Empty;
        public IList<DocumentStructure> Structure { get; set; } = new List<DocumentStructure>();
        public IList<ContentType> ContentType { get; set; } = new List<ContentType>();
        public IList<int> TableIndex { get; set; } = new List<int>();
    }

    public enum ContentType
    {
        NotProcessed = 0,
        TitleStart = 1,
        TitleChar = 2,
        TitleEnd = 3,
        SectionHeadingStart = 4,
        SectionHeadingChar = 5,
        SectionHeadingEnd = 6,
        TextStart = 7,
        TextChar = 8,
        TextEnd = 9,
        TableStart = 10,
        TableChar = 11,
        TableEnd = 12
    }
}
