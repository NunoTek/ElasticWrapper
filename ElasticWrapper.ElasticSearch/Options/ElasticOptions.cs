namespace ElasticWrapper.ElasticSearch.Options
{
    public class ElasticOptions
    {
        public string? LogsPath { get; set; }

        public string Uri { get; set; }

        public string? CloudId { get; set; }

        public string? UserName { get; set; }

        public string? Password { get; set; }

        public string Index { get; set; }

        public bool UseRollOverAlias { get; set; } = false;

        public string? Pattern { get; set; }

        public int MaxSizeGb { get; set; } = 10;

        public long? MaxDocuments { get; set; }

        public int MaxInnerResultWindow { get; set; } = 1000;
    }
}
