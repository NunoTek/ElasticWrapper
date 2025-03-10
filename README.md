# ElasticWrapper.ElasticSearch

A powerful and flexible .NET wrapper library for Elasticsearch that simplifies the integration and usage of Elasticsearch in .NET applications. This library provides an abstraction layer over the NEST client with additional features and conveniences for common Elasticsearch operations.

## Features

- Built on top of the official NEST client for Elasticsearch
- Simplified repository pattern for Elasticsearch operations
- Built-in request builder for complex query construction
- Automatic retry policies using Polly
- Type-safe query building
- Extension methods for common operations
- Attribute-based configuration
- Flexible model mapping

## Prerequisites

- .NET 8.0 or later
- Elasticsearch 7.x (compatible with NEST 7.17.5)

## Installation

You can install the package via NuGet Package Manager:

```powershell
Install-Package ElasticWrapper.ElasticSearch
```

## Dependencies

- NEST (7.17.5)
- NEST.JsonNetSerializer (7.17.5)
- Polly (8.4.1)
- Polly.Extensions.Http (3.0.0)
- Microsoft.Extensions.Hosting.Abstractions (8.0.0)
- Microsoft.Extensions.Logging.Abstractions (8.0.2)
- System.ComponentModel.Annotations (5.0.0)

## Configuration Options

The `ElasticOptions` class provides various configuration settings to customize your Elasticsearch connection and behavior:

### Connection Settings
- `Uri`: The Elasticsearch server URL (e.g., "http://localhost:9200")
- `CloudId`: Optional Elastic Cloud deployment ID for cloud deployments
- `UserName`: Optional username for authentication
- `Password`: Optional password for authentication

### Index Settings
- `Index`: The default index name to use
- `UseRollOverAlias`: Enable/disable index rollover functionality (default: false)
- `Pattern`: Optional index pattern for rollover indices
- `MaxSizeGb`: Maximum size in GB for an index before rollover (default: 10)
- `MaxDocuments`: Optional maximum number of documents before index rollover
- `MaxInnerResultWindow`: Maximum number of results in inner hits (default: 1000)

### Logging Settings
- `LogsPath`: Optional path for storing Elasticsearch client logs

Example configuration:

```csharp
services.AddElasticWrapper(options =>
{
    // Basic Settings
    options.Uri = "http://localhost:9200";
    options.Index = "my-application";
    
    // Authentication (if needed)
    options.UserName = "elastic";
    options.Password = "your-secure-password";
    
    // Index Management
    options.UseRollOverAlias = true;
    options.Pattern = "my-application-{0}"; // Results in indices like my-application-000001
    options.MaxSizeGb = 5;
    options.MaxDocuments = 1000000;
    
    // Performance Settings
    options.MaxInnerResultWindow = 2000;
    
    // Logging
    options.LogsPath = "logs/elasticsearch";
});
```

### Cloud Configuration

For Elastic Cloud deployments, use the CloudId instead of Uri:

```csharp
services.AddElasticWrapper(options =>
{
    options.CloudId = "deployment:cloud-id";
    options.UserName = "elastic";
    options.Password = "your-cloud-password";
    options.Index = "my-cloud-application";
});
```

## Project Structure

```
ElasticWrapper.ElasticSearch/
├── Base/                  # Core functionality classes
│   ├── ElasticBaseRepository.cs
│   ├── ElasticRequestBuilder.cs
│   └── ElasticClientProvider.cs
├── Attributes/           # Custom attributes for configuration
├── Converters/          # Type converters and serialization
├── Extensions/          # Extension methods
├── Models/              # Data models and DTOs
└── Options/             # Configuration options
```

## Key Components

### ElasticBaseRepository

The base repository class that provides common CRUD operations and search functionality for Elasticsearch indices. It includes methods for:
- Document indexing
- Document updates
- Document deletion
- Search operations
- Bulk operations
- Index management

### ElasticRequestBuilder

A fluent builder for constructing Elasticsearch queries with type safety and convenience methods for:
- Query construction
- Filtering
- Aggregations
- Sorting
- Pagination

### ElasticClientProvider

Manages the Elasticsearch client configuration and connection, including:
- Client initialization
- Connection management
- Retry policies
- Error handling

## Usage

1. Configure the Elasticsearch client in your `Startup.cs` or `Program.cs`:

```csharp
services.AddElasticWrapper(options =>
{
    options.Urls = new[] { "http://localhost:9200" };
    options.DefaultIndex = "your-default-index";
});
```

2. Create your repository by inheriting from ElasticBaseRepository:

```csharp
public class UserRepository : ElasticBaseRepository<User>
{
    public UserRepository(IElasticClientProvider clientProvider) 
        : base(clientProvider)
    {
    }
}
```

3. Use the repository in your services:

```csharp
public class UserService
{
    private readonly UserRepository _repository;

    public UserService(UserRepository repository)
    {
        _repository = repository;
    }

    public async Task<User> GetUserAsync(string id)
    {
        return await _repository.GetByIdAsync(id);
    }
}
```

## Continuous Integration and Deployment

This project uses GitHub Actions for continuous integration and deployment. Two workflows are configured:

### Build Workflow
Triggered on:
- Push to main branch
- Pull requests to main branch
- Manual trigger

Actions performed:
- Builds the solution
- Runs unit tests
- Creates NuGet package
- Uploads package as artifact

### Release Workflow
Triggered on:
- Release publication

Actions performed:
- Builds the solution with release version
- Runs unit tests
- Creates versioned NuGet package
- Publishes to NuGet.org
- Uploads package as artifact

To create a new release:
1. Create and push a new tag following semantic versioning (e.g., v1.0.0)
2. Create a new release on GitHub using that tag
3. Publish the release
4. The workflow will automatically build and publish to NuGet.org

Note: Publishing to NuGet requires a NuGet API key stored in the repository secrets as `NUGET_API_KEY`.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Support

For support and questions, please open an issue in the GitHub repository. 