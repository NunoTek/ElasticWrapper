# ElasticWrapper.ElasticSearch

[![NuGet](https://img.shields.io/nuget/v/ElasticWrapper.ElasticSearch.svg)](https://www.nuget.org/packages/ElasticWrapper.ElasticSearch)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![Elasticsearch](https://img.shields.io/badge/Elasticsearch-8.x%20%2F%209.x-005571)](https://www.elastic.co/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

A powerful and flexible .NET wrapper library for Elasticsearch that simplifies the integration and usage of Elasticsearch in .NET applications. This library provides an abstraction layer over the official Elastic.Clients.Elasticsearch client with additional features and conveniences for common Elasticsearch operations.

## ✨ Features

- 🔌 Built on top of the official **Elastic.Clients.Elasticsearch** client (v9.x)
- 📦 Simplified repository pattern for Elasticsearch operations
- 🔍 Built-in request builder for complex query construction
- 🔄 Automatic retry policies using Polly
- 🛡️ Type-safe query building
- 🧩 Extension methods for common operations
- 🏷️ Attribute-based configuration
- 🗺️ Flexible model mapping
- ☁️ Support for both single-node and Elastic Cloud deployments
- 📊 Aggregation support with automatic result parsing

## 📋 Prerequisites

- .NET 10.0 or later
- Elasticsearch 8.x / 9.x (compatible with Elastic.Clients.Elasticsearch 9.2.2)

## 📦 Installation

Install via NuGet Package Manager:

```powershell
Install-Package ElasticWrapper.ElasticSearch
```

Or via .NET CLI:

```bash
dotnet add package ElasticWrapper.ElasticSearch
```

## 🚀 Quick Start

### 1. Define your entity

```csharp
public class Product : ElasticEntity<Guid>
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    
    [ElasticAggregate]
    public string Category { get; set; } = string.Empty;
}
```

### 2. Define your filter

```csharp
public class ProductFilter
{
    public string? Name { get; set; }
    public List<string>? Categories { get; set; }
    public ElasticRangeFilter? PriceRange { get; set; }
}
```

### 3. Configure services

```csharp
builder.Services.AddElasticWrapper<Product, ProductFilter, Guid>(options =>
{
    options.Uri = "http://localhost:9200";
    options.Index = "products";
});
```

### 4. Use the repository

```csharp
public class ProductService
{
    private readonly ElasticBaseRepository<Product, ProductFilter, Guid> _repository;

    public ProductService(ElasticBaseRepository<Product, ProductFilter, Guid> repository)
    {
        _repository = repository;
    }

    public async Task<Product?> GetByIdAsync(Guid id) 
        => await _repository.GetAsync(id);

    public async Task CreateAsync(Product product) 
        => await _repository.InsertAsync(product);
}
```

## 📚 Dependencies

| Package | Version |
|---------|---------|
| Elastic.Clients.Elasticsearch | 9.2.2 |
| Polly | 8.6.5 |
| Microsoft.Extensions.Hosting.Abstractions | 10.0.0 |
| Microsoft.Extensions.Logging.Abstractions | 10.0.0 |
| System.ComponentModel.Annotations | 5.0.0 |

## ⚙️ Configuration Options

The `ElasticOptions` class provides various configuration settings:

### Connection Settings

| Property | Description | Required |
|----------|-------------|----------|
| `Uri` | Elasticsearch server URL (e.g., `http://localhost:9200`) | Yes* |
| `CloudId` | Elastic Cloud deployment ID | Yes* |
| `UserName` | Username for authentication | No |
| `Password` | Password for authentication | No |

*Either `Uri` or `CloudId` is required.

### Index Settings

| Property | Description | Default |
|----------|-------------|---------|
| `Index` | Default index name | Required |
| `UseRollOverAlias` | Enable index rollover | `false` |
| `Pattern` | Index pattern for rollover | `null` |
| `MaxSizeGb` | Max size before rollover | `10` |
| `MaxDocuments` | Max documents before rollover | `null` |
| `MaxInnerResultWindow` | Max inner hit results | `1000` |

### Example: Full Configuration

```csharp
services.AddElasticWrapper<MyEntity, MyFilters, Guid>(options =>
{
    // Basic Settings
    options.Uri = "http://localhost:9200";
    options.Index = "my-application";
    
    // Authentication (if needed)
    options.UserName = "elastic";
    options.Password = "your-secure-password";
    
    // Index Management
    options.UseRollOverAlias = true;
    options.Pattern = "my-application"; // Results in indices like my-application-000001
    options.MaxSizeGb = 5;
    options.MaxDocuments = 1000000;
    
    // Performance Settings
    options.MaxInnerResultWindow = 2000;
    
    // Logging
    options.LogsPath = "logs/elasticsearch";
});
```

### Example: Elastic Cloud Configuration

For Elastic Cloud deployments:

```csharp
services.AddElasticWrapper<MyEntity, MyFilters, Guid>(options =>
{
    options.CloudId = "deployment:your-cloud-id";
    options.UserName = "elastic";
    options.Password = "your-cloud-password";
    options.Index = "my-cloud-application";
});
```

## 📁 Project Structure

```
ElasticWrapper.ElasticSearch/
├── Attributes/           # Custom attributes for configuration
│   ├── ElasticAggregateAttribute.cs
│   ├── ElasticIgnoreOnBuildQueryAttribute.cs
│   └── NestedAttribute.cs
├── Base/                  # Core functionality classes
│   ├── ElasticBaseRepository.cs
│   ├── ElasticClientProvider.cs
│   └── ElasticRequestBuilder.cs
├── Converters/          # Type converters and serialization
│   └── ElasticJsonConverter.cs
├── Extensions/          # Extension methods
│   ├── ServiceCollectionExtensions.cs
│   └── StringExtensions.cs
├── Models/              # Data models and DTOs
│   ├── ElasticAggregateResult.cs
│   ├── ElasticDocumentProperty.cs
│   ├── ElasticEntity.cs
│   ├── ElasticPaging.cs
│   └── ElasticRangeFilter.cs
└── Options/             # Configuration options
    └── ElasticOptions.cs
```

## 🧱 Key Components

### ElasticBaseRepository&lt;TEntity, TFilters, TKey&gt;

The base repository class providing common operations:

| Category | Methods |
|----------|---------|
| **Cluster** | `HealthAsync` |
| **Index** | `IndicesExistsAsync`, `CreateIndiceAsync`, `DeleteIndiceAsync`, `IndicesSizeAsync`, `IndicesStatsAsync` |
| **Documents** | `GetAsync`, `InsertAsync`, `UpdateAsync`, `UpdatePartialAsync`, `DeleteAsync`, `ExistsAsync` |
| **Search** | `SearchAsync`, `CountAsync`, `AnyAsync`, `GetAggregationsAsync` |
| **Bulk** | `BulkInsertAsync`, `BulkUpdateAsync`, `BulkDeleteAsync` |

### ElasticRequestBuilder&lt;TEntity, TFilters&gt;

A fluent builder for constructing Elasticsearch queries:

- **Query Types**: Bool queries, terms, range, query string, nested
- **Automatic Field Mapping**: Converts property names to camelCase
- **Aggregations**: Terms, min, max, avg, nested aggregations
- **Sorting**: Field sorting with nested path support
- **Pagination**: Size and from parameters

### ElasticClientProvider&lt;TEntity&gt;

Manages Elasticsearch client configuration:

- Client initialization with `ElasticsearchClient` (v9.x)
- Single-node and Elastic Cloud support
- Basic authentication
- Debug mode with request/response logging
- Configurable request timeout

### ElasticEntity&lt;TKey&gt;

Base entity class for Elasticsearch documents:

```csharp
// Use default Guid key
public class MyDocument : ElasticEntity { }

// Or specify custom key type
public class MyDocument : ElasticEntity<int> { }
```

### 🏷️ Custom Attributes

| Attribute | Description | Example |
|-----------|-------------|---------|
| `[ElasticAggregate]` | Mark property for aggregation | `[ElasticAggregate] public string Category { get; set; }` |
| `[ElasticAggregate("groupField")]` | Aggregate with custom group | `[ElasticAggregate("category.keyword")]` |
| `[ElasticIgnoreOnBuildQuery]` | Exclude from query building | `[ElasticIgnoreOnBuildQuery] public string InternalField { get; set; }` |
| `[Nested]` | Map to nested Elasticsearch field | `[Nested("items")] public List<Item> Items { get; set; }` |

## 📖 Usage Examples

### Basic CRUD Operations

```csharp
// Create
await _repository.InsertAsync(new Product { Id = Guid.NewGuid(), Name = "Widget" });

// Read
var product = await _repository.GetAsync(productId);

// Update
product.Name = "Updated Widget";
await _repository.UpdateAsync(product.Id, product);

// Partial Update
await _repository.UpdatePartialAsync(productId, new { Name = "Partial Update" });

// Delete
await _repository.DeleteAsync(productId);
```

### Search with Filters

```csharp
var filter = new ProductFilter
{
    Name = "widget",
    Categories = new List<string> { "Electronics", "Gadgets" },
    PriceRange = new ElasticRangeFilter { Min = 10, Max = 100 }
};

var paging = new ElasticPaging
{
    From = 0,
    Size = 20,
    SortBy = "Price",
    Descending = false
};

var results = await _repository.SearchAsync(filter, paging);

foreach (var hit in results.Documents)
{
    Console.WriteLine($"{hit.Name}: ${hit.Price}");
}
```

### Aggregations

```csharp
// Get aggregations based on [ElasticAggregate] attributes
var aggregations = await _repository.GetAggregationsAsync(filter);

foreach (var agg in aggregations)
{
    Console.WriteLine($"Aggregation: {agg.Key}");
    foreach (var option in agg.Options)
    {
        Console.WriteLine($"  {option.Key}: {option.Count}");
    }
}
```

### Bulk Operations

```csharp
var products = Enumerable.Range(1, 10000)
    .Select(i => new Product { Id = Guid.NewGuid(), Name = $"Product {i}" });

// Bulk insert with automatic chunking
await _repository.BulkInsertAsync(products);

// Bulk update
await _repository.BulkUpdateAsync(updatedProducts);

// Bulk delete
await _repository.BulkDeleteAsync(new[] { 1, 2, 3, 4, 5 });
```

### Index Management

```csharp
// Check if index exists
if (!await _repository.IndicesExistsAsync())
{
    await _repository.CreateIndiceAsync();
}

// Get index statistics
var stats = await _repository.IndicesStatsAsync();
Console.WriteLine($"Documents: {stats?.Docs?.Count}");
Console.WriteLine($"Size: {stats?.Store?.SizeInBytes} bytes");

// Check cluster health
var health = await _repository.HealthAsync();
Console.WriteLine($"Status: {health.Status}");
```

## 🔄 Migration from NEST

This library has been migrated from NEST to the official `Elastic.Clients.Elasticsearch` client.

### Key Changes

| NEST (v7.x) | Elastic.Clients.Elasticsearch (v9.x) |
|-------------|--------------------------------------|
| `ElasticClient` | `ElasticsearchClient` |
| `ConnectionSettings` | `ElasticsearchClientSettings` |
| `response.IsValid` | `response.IsValidResponse` |
| `Nest` namespace | `Elastic.Clients.Elasticsearch` |
| Newtonsoft.Json | System.Text.Json |

### Namespace Changes

```csharp
// Before (NEST)
using Nest;

// After (Elastic.Clients.Elasticsearch)
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Elastic.Clients.Elasticsearch.Aggregations;
```

## 🔧 CI/CD

This project uses GitHub Actions for continuous integration and deployment.

### Build Workflow

**Triggers:** Push to main, Pull requests, Manual

| Step | Action |
|------|--------|
| ✅ | Build solution |
| ✅ | Run unit tests |
| ✅ | Create NuGet package |
| ✅ | Upload artifact |

### Release Workflow

**Trigger:** Release publication

| Step | Action |
|------|--------|
| ✅ | Build with release version |
| ✅ | Run tests |
| ✅ | Publish to NuGet.org |

### Creating a Release

```bash
# 1. Tag your release
git tag v1.0.0
git push origin v1.0.0

# 2. Create release on GitHub
# 3. Publish - workflow auto-deploys to NuGet
```

> **Note:** Requires `NUGET_API_KEY` in repository secrets.

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 💬 Support

For support and questions, please [open an issue](https://github.com/NunoTek/ElasticWrapper/issues) in the GitHub repository.

---

Made with ❤️ by [Nuno ARAUJO](https://github.com/NunoTek)