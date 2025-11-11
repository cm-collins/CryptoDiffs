using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Serilog;

namespace CryptoDiffs;

/// <summary>
/// Service for logging execution data to Cosmos DB.
/// Logs all stages, errors, and execution details for comprehensive tracking.
/// </summary>
public class CosmosDbLoggingService
{
    private readonly CosmosClient _cosmosClient;
    private readonly Container _container;
    private readonly AppSettings _settings;
    private readonly bool _enabled;

    public CosmosDbLoggingService(IOptions<AppSettings> settings)
    {
        _settings = settings.Value;
        _enabled = !string.IsNullOrWhiteSpace(_settings.CosmosDbConnectionString) &&
                   !string.IsNullOrWhiteSpace(_settings.CosmosDbDatabaseName) &&
                   !string.IsNullOrWhiteSpace(_settings.CosmosDbContainerName);

        if (!_enabled)
        {
            Log.Warning("Cosmos DB logging is disabled. Configure COSMOS_DB_CONNECTION_STRING, COSMOS_DB_DATABASE_NAME, and COSMOS_DB_CONTAINER_NAME to enable.");
            _cosmosClient = null!;
            _container = null!;
            return;
        }

        try
        {
            var cosmosClientOptions = new CosmosClientOptions
            {
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                }
            };
            _cosmosClient = new CosmosClient(_settings.CosmosDbConnectionString, cosmosClientOptions);
            var database = _cosmosClient.GetDatabase(_settings.CosmosDbDatabaseName);
            _container = database.GetContainer(_settings.CosmosDbContainerName);
            Log.Information("Cosmos DB logging initialized. Database: {Database}, Container: {Container}",
                _settings.CosmosDbDatabaseName, _settings.CosmosDbContainerName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize Cosmos DB client");
            _enabled = false;
            _cosmosClient = null!;
            _container = null!;
        }
    }

    /// <summary>
    /// Logs an execution entry (HTTP or Timer trigger).
    /// </summary>
    public async Task<string> LogExecutionAsync(ExecutionLogEntry entry)
    {
        if (!_enabled) return entry.Id;

        try
        {
            entry.TenantId = "default";
            var response = await _container.CreateItemAsync(entry, new PartitionKey(entry.TenantId));
            Log.Debug("Logged execution entry: {ExecutionId}, Status: {Status}", entry.ExecutionId, entry.Status);
            return entry.Id;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to log execution entry to Cosmos DB");
            return entry.Id;
        }
    }

    /// <summary>
    /// Logs a stage entry (individual operation step).
    /// </summary>
    public async Task<string> LogStageAsync(StageLogEntry entry)
    {
        if (!_enabled) return entry.Id;

        try
        {
            entry.TenantId = "default";
            var response = await _container.CreateItemAsync(entry, new PartitionKey(entry.TenantId));
            Log.Debug("Logged stage entry: {StageName}, Status: {Status}, ExecutionId: {ExecutionId}",
                entry.StageName, entry.Status, entry.ExecutionId);
            return entry.Id;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to log stage entry to Cosmos DB");
            return entry.Id;
        }
    }

    /// <summary>
    /// Creates an error info object from an exception.
    /// </summary>
    public static ErrorInfo CreateErrorInfo(Exception ex)
    {
        return new ErrorInfo
        {
            Message = ex.Message,
            ErrorType = ex.GetType().Name,
            StackTrace = ex.StackTrace,
            InnerError = ex.InnerException != null ? CreateErrorInfo(ex.InnerException) : null
        };
    }

    /// <summary>
    /// Creates a stage log entry with error information.
    /// </summary>
    public static StageLogEntry CreateErrorStageLog(string executionId, string stageName, Exception ex, long durationMs = 0)
    {
        return new StageLogEntry
        {
            ExecutionId = executionId,
            StageName = stageName,
            Status = "Failed",
            DurationMs = durationMs,
            Error = CreateErrorInfo(ex),
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a successful stage log entry.
    /// </summary>
    public static StageLogEntry CreateSuccessStageLog(string executionId, string stageName, Dictionary<string, object>? inputData = null, Dictionary<string, object>? outputData = null, Dictionary<string, object>? metadata = null, long durationMs = 0)
    {
        return new StageLogEntry
        {
            ExecutionId = executionId,
            StageName = stageName,
            Status = "Success",
            DurationMs = durationMs,
            InputData = inputData,
            OutputData = outputData,
            Metadata = metadata,
            Timestamp = DateTime.UtcNow
        };
    }
}

