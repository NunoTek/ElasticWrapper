using Nest;
using System;
using System.ComponentModel.DataAnnotations;

namespace ElasticWrapper.ElasticSearch.Models
{
    public class ElasticEntity : ElasticEntity<Guid>
    {
    }

    [ElasticsearchType(IdProperty = nameof(ElasticEntity.Id))]
    public class ElasticEntity<T> : IElasticEntity<T>
    {
        [Key]
        public virtual T Id { get; set; }
    }

    public interface IElasticEntity<out TKey>
    {
        public TKey Id { get; }
    }
}