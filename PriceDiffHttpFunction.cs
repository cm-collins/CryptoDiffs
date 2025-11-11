using System.Text.Json;
using System.Text;
using System.IO;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.WebUtilities;
using Serilog;

namespace CryptoDiffs;

public class PriceDiffHttpFunction
{
    private readonly ILogger<PriceDiffHttpFunction> _logger;
    private readonly AppSettings _settings;
    private readonly BinanceService _binanceService;
    private readonly CalculationService _calculationService;
    private readonly ExcelService _excelService;
    private readonly EmailService _emailService;
    private readonly CosmosDbLoggingService _cosmosLogger;

    public PriceDiffHttpFunction(
        ILogger<PriceDiffHttpFunction> logger,
        IOptions<AppSettings> settings,
        BinanceService binanceService,
        CalculationService calculationService,
        ExcelService excelService,
        EmailService emailService,
        CosmosDbLoggingService cosmosLogger)
    {
        _logger = logger;
        _settings = settings.Value;
        _binanceService = binanceService;
        _calculationService = calculationService;
        _excelService = excelService;
        _emailService = emailService;
        _cosmosLogger = cosmosLogger;
    }

    [Function("PriceDiffHttpFunction")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req)
    {
        var executionId = Guid.NewGuid().ToString();
        var executionStartTime = DateTime.UtcNow;
        var executionLog = new ExecutionLogEntry
        {
            ExecutionId = executionId,
            Trigger = "http",
            HttpMethod = req.Method,
            Status = "InProgress"
        };
        var stageLogs = new List<string>();

        _logger.LogInformation("HTTP trigger function processed a request. Method: {Method}, ExecutionId: {ExecutionId}", req.Method, executionId);

        try
        {
            // Stage: Request Parsing
            var parseStageStart = DateTime.UtcNow;
            var request = await ParseRequestAsync(req);
            var parseStage = CosmosDbLoggingService.CreateSuccessStageLog(
                executionId,
                "RequestParsing",
                inputData: new Dictionary<string, object> { { "method", req.Method }, { "url", req.Url.ToString() } },
                outputData: new Dictionary<string, object> { { "symbol", request.Symbol ?? "null" }, { "periods", request.Periods ?? "null" } },
                durationMs: (long)(DateTime.UtcNow - parseStageStart).TotalMilliseconds);
            var parseStageId = await _cosmosLogger.LogStageAsync(parseStage);
            stageLogs.Add(parseStageId);
            
            // Apply defaults from settings
            request.Symbol ??= _settings.DefaultSymbol;
            request.Periods ??= _settings.DefaultPeriods;
            request.Interval ??= _settings.DefaultInterval;
            request.Aggregate ??= _settings.DefaultAggregate;
            request.Report ??= "json";

            // Stage: Validation
            var validationStageStart = DateTime.UtcNow;
            var validationResult = Validation.ValidateRequest(
                request,
                out var parsedPeriods,
                out var parsedAsOf);

            StageLogEntry validationStage;
            if (!validationResult.IsValid)
            {
                validationStage = new StageLogEntry
                {
                    ExecutionId = executionId,
                    StageName = "Validation",
                    Status = "Failed",
                    DurationMs = (long)(DateTime.UtcNow - validationStageStart).TotalMilliseconds,
                    Error = new ErrorInfo { Message = validationResult.ErrorMessage! },
                    InputData = new Dictionary<string, object> { { "request", request } }
                };
                var validationStageId = await _cosmosLogger.LogStageAsync(validationStage);
                stageLogs.Add(validationStageId);

                executionLog.Status = "Failed";
                executionLog.Error = new ErrorInfo { Message = validationResult.ErrorMessage! };
                executionLog.DurationMs = (long)(DateTime.UtcNow - executionStartTime).TotalMilliseconds;
                executionLog.StageLogIds = stageLogs;
                await _cosmosLogger.LogExecutionAsync(executionLog);

                return await CreateErrorResponseAsync(req, 400, validationResult.ErrorMessage!);
            }

            validationStage = CosmosDbLoggingService.CreateSuccessStageLog(
                executionId,
                "Validation",
                inputData: new Dictionary<string, object> { { "request", request } },
                outputData: new Dictionary<string, object> { { "periods", string.Join(",", parsedPeriods) }, { "asOf", parsedAsOf?.ToString() ?? "null" } },
                durationMs: (long)(DateTime.UtcNow - validationStageStart).TotalMilliseconds);
            var validationStageIdSuccess = await _cosmosLogger.LogStageAsync(validationStage);
            stageLogs.Add(validationStageIdSuccess);

            // Determine asOf date
            var asOf = parsedAsOf ?? DateTime.UtcNow.Date.AddDays(-1); // Last closed daily candle

            // Stage: Binance Fetch
            var binanceStageStart = DateTime.UtcNow;
            var maxPeriod = parsedPeriods.Max();
            List<Kline> klines;
            StageLogEntry binanceStage;

            try
            {
                klines = await _binanceService.GetKlinesAsync(
                    request.Symbol!,
                    request.Interval!,
                    maxPeriod,
                    asOf);

                if (klines.Count == 0)
                {
                    binanceStage = new StageLogEntry
                    {
                        ExecutionId = executionId,
                        StageName = "BinanceFetch",
                        Status = "Failed",
                        DurationMs = (long)(DateTime.UtcNow - binanceStageStart).TotalMilliseconds,
                        Error = new ErrorInfo { Message = "No klines data found for the specified parameters" },
                        InputData = new Dictionary<string, object>
                        {
                            { "symbol", request.Symbol! },
                            { "interval", request.Interval! },
                            { "maxPeriod", maxPeriod },
                            { "asOf", asOf.ToString() }
                        }
                    };
                    var binanceStageId = await _cosmosLogger.LogStageAsync(binanceStage);
                    stageLogs.Add(binanceStageId);

                    executionLog.Status = "Failed";
                    executionLog.Error = new ErrorInfo { Message = "No klines data found" };
                    executionLog.DurationMs = (long)(DateTime.UtcNow - executionStartTime).TotalMilliseconds;
                    executionLog.StageLogIds = stageLogs;
                    await _cosmosLogger.LogExecutionAsync(executionLog);

                    return await CreateErrorResponseAsync(req, 404, "No klines data found for the specified parameters");
                }

                binanceStage = CosmosDbLoggingService.CreateSuccessStageLog(
                    executionId,
                    "BinanceFetch",
                    inputData: new Dictionary<string, object>
                    {
                        { "symbol", request.Symbol! },
                        { "interval", request.Interval! },
                        { "maxPeriod", maxPeriod },
                        { "asOf", asOf.ToString() }
                    },
                    outputData: new Dictionary<string, object> { { "klinesCount", klines.Count } },
                    durationMs: (long)(DateTime.UtcNow - binanceStageStart).TotalMilliseconds);
                var binanceStageIdSuccess = await _cosmosLogger.LogStageAsync(binanceStage);
                stageLogs.Add(binanceStageIdSuccess);
            }
            catch (Exception ex)
            {
                binanceStage = CosmosDbLoggingService.CreateErrorStageLog(executionId, "BinanceFetch", ex, (long)(DateTime.UtcNow - binanceStageStart).TotalMilliseconds);
                var binanceStageId = await _cosmosLogger.LogStageAsync(binanceStage);
                stageLogs.Add(binanceStageId);

                executionLog.Status = "Failed";
                executionLog.Error = CosmosDbLoggingService.CreateErrorInfo(ex);
                executionLog.DurationMs = (long)(DateTime.UtcNow - executionStartTime).TotalMilliseconds;
                executionLog.StageLogIds = stageLogs;
                await _cosmosLogger.LogExecutionAsync(executionLog);

                throw;
            }

            // Stage: Calculation
            var calculationStageStart = DateTime.UtcNow;
            var results = _calculationService.CalculatePeriodResults(
                klines,
                parsedPeriods,
                request.Aggregate!,
                asOf,
                request.Interval!);

            if (results.Count == 0)
            {
                var calcStage = new StageLogEntry
                {
                    ExecutionId = executionId,
                    StageName = "Calculation",
                    Status = "Failed",
                    DurationMs = (long)(DateTime.UtcNow - calculationStageStart).TotalMilliseconds,
                    Error = new ErrorInfo { Message = "No results calculated. Insufficient data for requested periods" },
                    InputData = new Dictionary<string, object>
                    {
                        { "klinesCount", klines.Count },
                        { "periods", string.Join(",", parsedPeriods) },
                        { "aggregate", request.Aggregate! }
                    }
                };
                var calcStageId = await _cosmosLogger.LogStageAsync(calcStage);
                stageLogs.Add(calcStageId);

                executionLog.Status = "Failed";
                executionLog.Error = new ErrorInfo { Message = "No results calculated" };
                executionLog.DurationMs = (long)(DateTime.UtcNow - executionStartTime).TotalMilliseconds;
                executionLog.StageLogIds = stageLogs;
                await _cosmosLogger.LogExecutionAsync(executionLog);

                return await CreateErrorResponseAsync(req, 404, "No results calculated. Insufficient data for requested periods");
            }

            var calculationStage = CosmosDbLoggingService.CreateSuccessStageLog(
                executionId,
                "Calculation",
                inputData: new Dictionary<string, object>
                {
                    { "klinesCount", klines.Count },
                    { "periods", string.Join(",", parsedPeriods) },
                    { "aggregate", request.Aggregate! }
                },
                outputData: new Dictionary<string, object> { { "resultsCount", results.Count } },
                durationMs: (long)(DateTime.UtcNow - calculationStageStart).TotalMilliseconds);
            var calculationStageId = await _cosmosLogger.LogStageAsync(calculationStage);
            stageLogs.Add(calculationStageId);

            // Build response
            var response = new PriceDiffResponse
            {
                Symbol = request.Symbol!,
                AsOf = asOf,
                Interval = request.Interval!,
                Aggregate = request.Aggregate!,
                Results = results,
                Notes = new List<string>
                {
                    "Uses last fully closed daily candle.",
                    "Binance public klines."
                }
            };

            // Stage: Excel Generation (always generate for HTTP trigger)
            var excelStageStart = DateTime.UtcNow;
            byte[]? excelBytes = null;
            string? excelFileName = null;
            try
            {
                excelBytes = _excelService.GenerateExcelReport(response);
                excelFileName = $"cryptodiffs-{response.Symbol}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xlsx";
                
                // Save Excel file to runs directory
                var currentDir = Directory.GetCurrentDirectory();
                var rootDirectory = currentDir;
                
                // If running from bin/output, go up to project root
                if (currentDir.Contains("bin") || currentDir.Contains("output"))
                {
                    rootDirectory = Directory.GetParent(currentDir)?.Parent?.Parent?.FullName ?? currentDir;
                }
                
                var runsDirectory = Path.Combine(rootDirectory, "runs");
                Directory.CreateDirectory(runsDirectory);
                var excelFilePath = Path.Combine(runsDirectory, excelFileName);
                await File.WriteAllBytesAsync(excelFilePath, excelBytes);
                Log.Information("Excel file saved to: {FilePath}", excelFilePath);
                
                var excelStage = CosmosDbLoggingService.CreateSuccessStageLog(
                    executionId,
                    "ExcelGeneration",
                    inputData: new Dictionary<string, object> { { "resultsCount", results.Count } },
                    outputData: new Dictionary<string, object> 
                    { 
                        { "fileSizeBytes", excelBytes.Length },
                        { "fileName", excelFileName },
                        { "filePath", excelFilePath }
                    },
                    durationMs: (long)(DateTime.UtcNow - excelStageStart).TotalMilliseconds);
                var excelStageId = await _cosmosLogger.LogStageAsync(excelStage);
                stageLogs.Add(excelStageId);
            }
            catch (Exception ex)
            {
                var excelStage = CosmosDbLoggingService.CreateErrorStageLog(executionId, "ExcelGeneration", ex, (long)(DateTime.UtcNow - excelStageStart).TotalMilliseconds);
                var excelStageId = await _cosmosLogger.LogStageAsync(excelStage);
                stageLogs.Add(excelStageId);
                Log.Error(ex, "Failed to generate Excel report for HTTP trigger");
            }

            // Stage: Email Sending (always send for HTTP trigger)
            EmailLogInfo? emailInfo = null;
            var emailStageStart = DateTime.UtcNow;
            try
            {
                if (excelBytes != null && !string.IsNullOrWhiteSpace(excelFileName))
                {
                    emailInfo = await SendEmailWithReportAsync(response, request, excelBytes, excelFileName, executionId);
                }
                else
                {
                    // Generate Excel if not already generated
                    excelBytes = _excelService.GenerateExcelReport(response);
                    excelFileName = $"cryptodiffs-{response.Symbol}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xlsx";
                    emailInfo = await SendEmailWithReportAsync(response, request, excelBytes, excelFileName, executionId);
                }
                
                var emailStage = CosmosDbLoggingService.CreateSuccessStageLog(
                    executionId,
                    "EmailSending",
                    inputData: new Dictionary<string, object>
                    {
                        { "to", _settings.MailTo },
                        { "cc", _settings.MailCc ?? "null" },
                        { "bcc", _settings.MailBcc ?? "null" },
                        { "subject", emailInfo.Subject }
                    },
                    outputData: new Dictionary<string, object>
                    {
                        { "sent", emailInfo.Sent },
                        { "messageId", emailInfo.MessageId ?? "null" }
                    },
                    durationMs: (long)(DateTime.UtcNow - emailStageStart).TotalMilliseconds);
                var emailStageId = await _cosmosLogger.LogStageAsync(emailStage);
                stageLogs.Add(emailStageId);
            }
            catch (Exception ex)
            {
                var emailStage = CosmosDbLoggingService.CreateErrorStageLog(executionId, "EmailSending", ex, (long)(DateTime.UtcNow - emailStageStart).TotalMilliseconds);
                emailStage.InputData = new Dictionary<string, object>
                {
                    { "to", _settings.MailTo },
                    { "cc", _settings.MailCc ?? "null" },
                    { "bcc", _settings.MailBcc ?? "null" }
                };
                var emailStageId = await _cosmosLogger.LogStageAsync(emailStage);
                stageLogs.Add(emailStageId);
                Log.Error(ex, "Failed to send email from HTTP trigger");
            }

            // Finalize execution log
            executionLog.Status = "Success";
            executionLog.DurationMs = (long)(DateTime.UtcNow - executionStartTime).TotalMilliseconds;
            executionLog.Request = request;
            executionLog.Symbol = request.Symbol;
            executionLog.Periods = parsedPeriods;
            executionLog.Response = response;
            executionLog.EmailInfo = emailInfo;
            executionLog.StageLogIds = stageLogs;
            await _cosmosLogger.LogExecutionAsync(executionLog);

            // Handle report format (always return JSON with execution summary, Excel is saved to disk)
            var reportFormat = request.Report?.ToLower() ?? "json";
            return reportFormat switch
            {
                "excel" => await CreateExcelResponseAsync(req, response, excelBytes, excelFileName),
                "none" => await CreateSuccessResponseAsync(req, new 
                { 
                    message = "Calculation completed successfully. Excel file saved and email sent.",
                    executionId = executionId,
                    excelFile = excelFileName,
                    emailSent = emailInfo?.Sent ?? false
                }),
                _ => await CreateJsonResponseAsync(req, response, executionId, excelFileName, emailInfo)
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing HTTP request. ExecutionId: {ExecutionId}", executionId);
            _logger.LogError(ex, "Error processing HTTP request. ExecutionId: {ExecutionId}", executionId);

            // Log failed execution
            executionLog.Status = "Failed";
            executionLog.Error = CosmosDbLoggingService.CreateErrorInfo(ex);
            executionLog.DurationMs = (long)(DateTime.UtcNow - executionStartTime).TotalMilliseconds;
            executionLog.StageLogIds = stageLogs;
            await _cosmosLogger.LogExecutionAsync(executionLog);

            return await CreateErrorResponseAsync(req, 500, $"Internal server error: {ex.Message}");
        }
    }

    private async Task<PriceDiffRequest> ParseRequestAsync(HttpRequestData req)
    {
        var request = new PriceDiffRequest();

        // Parse query string parameters (for GET requests)
        if (!string.IsNullOrEmpty(req.Url.Query))
        {
            var queryParams = QueryHelpers.ParseQuery(req.Url.Query);
            
            if (queryParams.TryGetValue("symbol", out var symbol))
                request.Symbol = symbol.ToString();
            
            if (queryParams.TryGetValue("periods", out var periods))
                request.Periods = periods.ToString();
            
            if (queryParams.TryGetValue("asOf", out var asOf))
                request.AsOf = asOf.ToString();
            
            if (queryParams.TryGetValue("interval", out var interval))
                request.Interval = interval.ToString();
            
            if (queryParams.TryGetValue("aggregate", out var aggregate))
                request.Aggregate = aggregate.ToString();
            
            if (queryParams.TryGetValue("email", out var email) && bool.TryParse(email, out var emailValue))
                request.Email = emailValue;
            
            if (queryParams.TryGetValue("report", out var report))
                request.Report = report.ToString();
        }

        // Parse JSON body (for POST requests, overrides query params)
        if (req.Method == "POST" && req.Body != null)
        {
            try
            {
                using var reader = new StreamReader(req.Body, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                
                if (!string.IsNullOrWhiteSpace(body))
                {
                    var jsonRequest = JsonSerializer.Deserialize<PriceDiffRequest>(body, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (jsonRequest != null)
                    {
                        // Merge: JSON body overrides query params
                        request.Symbol = jsonRequest.Symbol ?? request.Symbol;
                        request.Periods = jsonRequest.Periods ?? request.Periods;
                        request.AsOf = jsonRequest.AsOf ?? request.AsOf;
                        request.Interval = jsonRequest.Interval ?? request.Interval;
                        request.Aggregate = jsonRequest.Aggregate ?? request.Aggregate;
                        request.Email = jsonRequest.Email ?? request.Email;
                        request.Report = jsonRequest.Report ?? request.Report;
                    }
                }
            }
            catch (JsonException ex)
            {
                Log.Warning(ex, "Failed to parse JSON body");
            }
        }

        return request;
    }

    private async Task<EmailLogInfo> SendEmailWithReportAsync(PriceDiffResponse response, PriceDiffRequest request, byte[] excelBytes, string fileName, string executionId)
    {
        try
        {

            // Build email subject
            var periodsStr = string.Join(",", response.Results.Select(r => $"{r.Days}d"));
            var subject = $"CryptoDiffs: {response.Symbol} ({periodsStr}) – {response.AsOf:yyyy-MM-dd} UTC";

            // Build email body
            var body = $@"
                <html>
                <body>
                    <h2>CryptoDiffs Price Difference Report</h2>
                    <p><strong>Symbol:</strong> {response.Symbol}</p>
                    <p><strong>As Of:</strong> {response.AsOf:yyyy-MM-dd HH:mm:ss} UTC</p>
                    <p><strong>Interval:</strong> {response.Interval}</p>
                    <p><strong>Aggregate Method:</strong> {response.Aggregate}</p>
                    <p>Please find the detailed Excel report attached.</p>
                </body>
                </html>";

            // Create email request
            var emailRequest = new EmailRequest
            {
                To = _settings.MailTo,
                Cc = _settings.MailCc,
                Bcc = _settings.MailBcc,
                From = _settings.MailFrom,
                Subject = subject,
                Body = body,
                Attachment = excelBytes,
                AttachmentFileName = fileName
            };

            var emailResponse = await _emailService.SendEmailAsync(emailRequest);
            
            var emailInfo = new EmailLogInfo
            {
                To = _settings.MailTo,
                Cc = _settings.MailCc,
                Bcc = _settings.MailBcc,
                From = _settings.MailFrom,
                Subject = subject,
                Sent = emailResponse.Success,
                MessageId = emailResponse.MessageId,
                AttachmentFileName = fileName,
                AttachmentSizeBytes = excelBytes.Length,
                Error = emailResponse.Error
            };

            if (!emailResponse.Success)
            {
                Log.Warning("Failed to send email: {Error}", emailResponse.Error);
            }
            else
            {
                Log.Information("Email sent successfully. MessageId: {MessageId}", emailResponse.MessageId);
            }

            return emailInfo;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error sending email");
            return new EmailLogInfo
            {
                To = _settings.MailTo,
                Cc = _settings.MailCc,
                Bcc = _settings.MailBcc,
                From = _settings.MailFrom,
                Sent = false,
                Error = ex.Message
            };
        }
    }

    private async Task<HttpResponseData> CreateJsonResponseAsync(HttpRequestData req, PriceDiffResponse response, string executionId, string? excelFileName, EmailLogInfo? emailInfo)
    {
        var httpResponse = req.CreateResponse(System.Net.HttpStatusCode.OK);
        httpResponse.Headers.Add("Content-Type", "application/json");

        var responseWithMetadata = new
        {
            executionId = executionId,
            excelFile = excelFileName,
            emailSent = emailInfo?.Sent ?? false,
            emailMessageId = emailInfo?.MessageId,
            data = response
        };

        var json = JsonSerializer.Serialize(responseWithMetadata, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await httpResponse.WriteStringAsync(json);
        return httpResponse;
    }

    private async Task<HttpResponseData> CreateExcelResponseAsync(HttpRequestData req, PriceDiffResponse response, byte[]? excelBytes, string? fileName)
    {
        // Use pre-generated Excel if available, otherwise generate new one
        if (excelBytes == null || string.IsNullOrWhiteSpace(fileName))
        {
            excelBytes = _excelService.GenerateExcelReport(response);
            fileName = $"cryptodiffs-{response.Symbol}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xlsx";
        }

        var httpResponse = req.CreateResponse(System.Net.HttpStatusCode.OK);
        httpResponse.Headers.Add("Content-Type", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        httpResponse.Headers.Add("Content-Disposition", $"attachment; filename=\"{fileName}\"");

        await httpResponse.Body.WriteAsync(excelBytes);
        return httpResponse;
    }

    private async Task<HttpResponseData> CreateSuccessResponseAsync(HttpRequestData req, object data)
    {
        var httpResponse = req.CreateResponse(System.Net.HttpStatusCode.OK);
        httpResponse.Headers.Add("Content-Type", "application/json");

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await httpResponse.WriteStringAsync(json);
        return httpResponse;
    }

    private async Task<HttpResponseData> CreateErrorResponseAsync(HttpRequestData req, int statusCode, string errorMessage)
    {
        var httpResponse = req.CreateResponse((System.Net.HttpStatusCode)statusCode);
        httpResponse.Headers.Add("Content-Type", "application/json");

        var errorResponse = new ErrorResponse
        {
            Error = errorMessage,
            ErrorCode = statusCode == 400 ? "VALIDATION_ERROR" : "SERVER_ERROR"
        };

        var json = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await httpResponse.WriteStringAsync(json);
        return httpResponse;
    }
}
