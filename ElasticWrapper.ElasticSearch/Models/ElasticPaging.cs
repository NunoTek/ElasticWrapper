namespace ElasticWrapper.ElasticSearch.Models
{
    public class ElasticPaging
    {
        public int From { get; set; } = 0;
        public int Size { get; set; } = 10;
        public string? SortBy { get; set; }
        public bool Descending { get; set; } = false;
    }
}