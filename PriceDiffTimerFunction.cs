using System;
using System.IO;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;

namespace CryptoDiffs;

public class PriceDiffTimerFunction
{
    private readonly Microsoft.Extensions.Logging.ILogger _logger;
    private readonly AppSettings _settings;
    private readonly BinanceService _binanceService;
    private readonly CalculationService _calculationService;
    private readonly ExcelService _excelService;
    private readonly EmailService _emailService;
    private readonly CosmosDbLoggingService _cosmosLogger;

    public PriceDiffTimerFunction(
        ILoggerFactory loggerFactory,
        IOptions<AppSettings> settings,
        BinanceService binanceService,
        CalculationService calculationService,
        ExcelService excelService,
        EmailService emailService,
        CosmosDbLoggingService cosmosLogger)
    {
        _logger = loggerFactory.CreateLogger<PriceDiffTimerFunction>();
        _settings = settings.Value;
        _binanceService = binanceService;
        _calculationService = calculationService;
        _excelService = excelService;
        _emailService = emailService;
        _cosmosLogger = cosmosLogger;
    }

    [Function("PriceDiffTimerFunction")]
    public async Task Run([TimerTrigger("0 */3 * * * *")] TimerInfo myTimer)
    {
        var executionId = Guid.NewGuid().ToString();
        var executionStartTime = DateTime.UtcNow;
        var executionLog = new ExecutionLogEntry
        {
            ExecutionId = executionId,
            Trigger = "timer",
            Status = "InProgress"
        };
        var stageLogs = new List<string>();

        _logger.LogInformation("Timer trigger function executed at: {executionTime}, ExecutionId: {ExecutionId}", DateTime.UtcNow, executionId);
        
        if (myTimer.ScheduleStatus is not null)
        {
            _logger.LogInformation("Next timer schedule at: {nextSchedule}", myTimer.ScheduleStatus.Next);
            executionLog.Metadata = new Dictionary<string, object>
            {
                { "nextSchedule", myTimer.ScheduleStatus.Next.ToString() }
            };
        }

        try
        {
            // Stage: Symbol Selection
            var symbolStageStart = DateTime.UtcNow;
            var symbols = _settings.RandomSymbols
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();

            if (symbols.Length == 0)
            {
                var symbolStage = new StageLogEntry
                {
                    ExecutionId = executionId,
                    StageName = "SymbolSelection",
                    Status = "Failed",
                    DurationMs = (long)(DateTime.UtcNow - symbolStageStart).TotalMilliseconds,
                    Error = new ErrorInfo { Message = "No random symbols configured" }
                };
                var symbolStageId = await _cosmosLogger.LogStageAsync(symbolStage);
                stageLogs.Add(symbolStageId);

                executionLog.Status = "Failed";
                executionLog.Error = new ErrorInfo { Message = "No random symbols configured" };
                executionLog.DurationMs = (long)(DateTime.UtcNow - executionStartTime).TotalMilliseconds;
                executionLog.StageLogIds = stageLogs;
                await _cosmosLogger.LogExecutionAsync(executionLog);

                Log.Warning("No random symbols configured");
                return;
            }

            var random = new Random();
            var symbol = symbols[random.Next(symbols.Length)];

            var symbolStageSuccess = CosmosDbLoggingService.CreateSuccessStageLog(
                executionId,
                "SymbolSelection",
                inputData: new Dictionary<string, object> { { "availableSymbols", string.Join(",", symbols) } },
                outputData: new Dictionary<string, object> { { "selectedSymbol", symbol } },
                durationMs: (long)(DateTime.UtcNow - symbolStageStart).TotalMilliseconds);
            var symbolStageIdSuccess = await _cosmosLogger.LogStageAsync(symbolStageSuccess);
            stageLogs.Add(symbolStageIdSuccess);

            Log.Information("Processing random symbol: {Symbol}", symbol);

            // Stage: Validation
            var validationStageStart = DateTime.UtcNow;
            var periodsResult = Validation.ValidatePeriods(_settings.DefaultPeriods, out var parsedPeriods);
            if (!periodsResult.IsValid)
            {
                var validationStage = new StageLogEntry
                {
                    ExecutionId = executionId,
                    StageName = "Validation",
                    Status = "Failed",
                    DurationMs = (long)(DateTime.UtcNow - validationStageStart).TotalMilliseconds,
                    Error = new ErrorInfo { Message = periodsResult.ErrorMessage! },
                    InputData = new Dictionary<string, object> { { "defaultPeriods", _settings.DefaultPeriods } }
                };
                var validationStageId = await _cosmosLogger.LogStageAsync(validationStage);
                stageLogs.Add(validationStageId);

                executionLog.Status = "Failed";
                executionLog.Error = new ErrorInfo { Message = periodsResult.ErrorMessage! };
                executionLog.DurationMs = (long)(DateTime.UtcNow - executionStartTime).TotalMilliseconds;
                executionLog.StageLogIds = stageLogs;
                await _cosmosLogger.LogExecutionAsync(executionLog);

                Log.Warning("Invalid default periods: {Error}", periodsResult.ErrorMessage);
                return;
            }

            var validationStageSuccess = CosmosDbLoggingService.CreateSuccessStageLog(
                executionId,
                "Validation",
                inputData: new Dictionary<string, object> { { "defaultPeriods", _settings.DefaultPeriods } },
                outputData: new Dictionary<string, object> { { "parsedPeriods", string.Join(",", parsedPeriods) } },
                durationMs: (long)(DateTime.UtcNow - validationStageStart).TotalMilliseconds);
            var validationStageIdSuccess = await _cosmosLogger.LogStageAsync(validationStageSuccess);
            stageLogs.Add(validationStageIdSuccess);

            // Determine asOf date (last closed daily candle)
            var asOf = DateTime.UtcNow.Date.AddDays(-1);

            // Stage: Binance Fetch
            var binanceStageStart = DateTime.UtcNow;
            var maxPeriod = parsedPeriods.Max();
            List<Kline> klines;

            try
            {
                klines = await _binanceService.GetKlinesAsync(
                    symbol,
                    _settings.DefaultInterval,
                    maxPeriod,
                    asOf);

                if (klines.Count == 0)
                {
                    var binanceStage = new StageLogEntry
                    {
                        ExecutionId = executionId,
                        StageName = "BinanceFetch",
                        Status = "Failed",
                        DurationMs = (long)(DateTime.UtcNow - binanceStageStart).TotalMilliseconds,
                        Error = new ErrorInfo { Message = "No klines data found" },
                        InputData = new Dictionary<string, object>
                        {
                            { "symbol", symbol },
                            { "interval", _settings.DefaultInterval },
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

                    Log.Warning("No klines data found for {Symbol}", symbol);
                    return;
                }

                var binanceStageSuccess = CosmosDbLoggingService.CreateSuccessStageLog(
                    executionId,
                    "BinanceFetch",
                    inputData: new Dictionary<string, object>
                    {
                        { "symbol", symbol },
                        { "interval", _settings.DefaultInterval },
                        { "maxPeriod", maxPeriod },
                        { "asOf", asOf.ToString() }
                    },
                    outputData: new Dictionary<string, object> { { "klinesCount", klines.Count } },
                    durationMs: (long)(DateTime.UtcNow - binanceStageStart).TotalMilliseconds);
                var binanceStageIdSuccess = await _cosmosLogger.LogStageAsync(binanceStageSuccess);
                stageLogs.Add(binanceStageIdSuccess);
            }
            catch (Exception ex)
            {
                var binanceStage = CosmosDbLoggingService.CreateErrorStageLog(executionId, "BinanceFetch", ex, (long)(DateTime.UtcNow - binanceStageStart).TotalMilliseconds);
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
                _settings.DefaultAggregate,
                asOf,
                _settings.DefaultInterval);

            if (results.Count == 0)
            {
                var calcStage = new StageLogEntry
                {
                    ExecutionId = executionId,
                    StageName = "Calculation",
                    Status = "Failed",
                    DurationMs = (long)(DateTime.UtcNow - calculationStageStart).TotalMilliseconds,
                    Error = new ErrorInfo { Message = "No results calculated" },
                    InputData = new Dictionary<string, object>
                    {
                        { "klinesCount", klines.Count },
                        { "periods", string.Join(",", parsedPeriods) },
                        { "aggregate", _settings.DefaultAggregate }
                    }
                };
                var calcStageId = await _cosmosLogger.LogStageAsync(calcStage);
                stageLogs.Add(calcStageId);

                executionLog.Status = "Failed";
                executionLog.Error = new ErrorInfo { Message = "No results calculated" };
                executionLog.DurationMs = (long)(DateTime.UtcNow - executionStartTime).TotalMilliseconds;
                executionLog.StageLogIds = stageLogs;
                await _cosmosLogger.LogExecutionAsync(executionLog);

                Log.Warning("No results calculated for {Symbol}", symbol);
                return;
            }

            var calculationStage = CosmosDbLoggingService.CreateSuccessStageLog(
                executionId,
                "Calculation",
                inputData: new Dictionary<string, object>
                {
                    { "klinesCount", klines.Count },
                    { "periods", string.Join(",", parsedPeriods) },
                    { "aggregate", _settings.DefaultAggregate }
                },
                outputData: new Dictionary<string, object> { { "resultsCount", results.Count } },
                durationMs: (long)(DateTime.UtcNow - calculationStageStart).TotalMilliseconds);
            var calculationStageId = await _cosmosLogger.LogStageAsync(calculationStage);
            stageLogs.Add(calculationStageId);

            // Build response
            var response = new PriceDiffResponse
            {
                Symbol = symbol,
                AsOf = asOf,
                Interval = _settings.DefaultInterval,
                Aggregate = _settings.DefaultAggregate,
                Results = results,
                Notes = new List<string>
                {
                    "Timer-triggered calculation.",
                    "Uses last fully closed daily candle.",
                    "Binance public klines."
                }
            };

            Log.Information("Calculated {Count} period results for {Symbol}", results.Count, symbol);

            // Stage: Excel Generation (always generate for timer trigger)
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
                Log.Error(ex, "Failed to generate Excel report for timer trigger");
            }

            // Stage: Email Sending (always send for timer trigger)
            EmailLogInfo? emailInfo = null;
            var emailStageStart = DateTime.UtcNow;
            try
            {
                if (excelBytes != null && !string.IsNullOrWhiteSpace(excelFileName))
                {
                    emailInfo = await SendEmailWithReportAsync(response, excelBytes, excelFileName, executionId);
                }
                else
                {
                    // Generate Excel if not already generated
                    excelBytes = _excelService.GenerateExcelReport(response);
                    excelFileName = $"cryptodiffs-{response.Symbol}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xlsx";
                    emailInfo = await SendEmailWithReportAsync(response, excelBytes, excelFileName, executionId);
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
                Log.Error(ex, "Failed to send email from timer trigger");
            }

            // Finalize execution log
            executionLog.Status = "Success";
            executionLog.DurationMs = (long)(DateTime.UtcNow - executionStartTime).TotalMilliseconds;
            executionLog.Symbol = symbol;
            executionLog.Periods = parsedPeriods;
            executionLog.Response = response;
            executionLog.EmailInfo = emailInfo;
            executionLog.StageLogIds = stageLogs;
            await _cosmosLogger.LogExecutionAsync(executionLog);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in timer trigger function. ExecutionId: {ExecutionId}", executionId);
            _logger.LogError(ex, "Error in timer trigger function. ExecutionId: {ExecutionId}", executionId);

            // Log failed execution
            executionLog.Status = "Failed";
            executionLog.Error = CosmosDbLoggingService.CreateErrorInfo(ex);
            executionLog.DurationMs = (long)(DateTime.UtcNow - executionStartTime).TotalMilliseconds;
            executionLog.StageLogIds = stageLogs;
            await _cosmosLogger.LogExecutionAsync(executionLog);
        }
    }

    private async Task<EmailLogInfo> SendEmailWithReportAsync(PriceDiffResponse response, byte[] excelBytes, string fileName, string executionId)
    {
        try
        {
            // Build email subject
            var periodsStr = string.Join(",", response.Results.Select(r => $"{r.Days}d"));
            var subject = $"CryptoDiffs: {response.Symbol} ({periodsStr}) – {response.AsOf:yyyy-MM-dd} UTC [Timer]";

            // Build email body
            var body = $@"
                <html>
                <body>
                    <h2>CryptoDiffs Price Difference Report (Timer Triggered)</h2>
                    <p><strong>Symbol:</strong> {response.Symbol}</p>
                    <p><strong>As Of:</strong> {response.AsOf:yyyy-MM-dd HH:mm:ss} UTC</p>
                    <p><strong>Interval:</strong> {response.Interval}</p>
                    <p><strong>Aggregate Method:</strong> {response.Aggregate}</p>
                    <p>This is an automated report generated by the timer trigger.</p>
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
                Log.Information("Email sent successfully via timer trigger. MessageId: {MessageId}", emailResponse.MessageId);
            }

            return emailInfo;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error sending email from timer trigger");
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
}
