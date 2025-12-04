using System;
using System.ComponentModel.DataAnnotations;

namespace ElasticWrapper.ElasticSearch.Models
{
    public class ElasticEntity : ElasticEntity<Guid>
    {
    }

    public class ElasticEntity<T> : IElasticEntity<T>
    {
        [Key]
        public virtual T Id { get; set; } = default!;
    }

    public interface IElasticEntity<out TKey>
    {
        TKey Id { get; }
    }
}