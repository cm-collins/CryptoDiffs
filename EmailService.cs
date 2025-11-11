using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Extensions.Options;
using Serilog;

namespace CryptoDiffs;

/// <summary>
/// Service for sending emails via Microsoft Graph API.
/// Supports To, CC, and BCC recipients with Excel attachments.
/// Saves Excel files to root directory before sending.
/// </summary>
public class EmailService
{
    private readonly AppSettings _settings;
    private readonly GraphServiceClient? _graphClient;
    private const int MaxAttachmentSizeBytes = 25 * 1024 * 1024; // 25 MB

    public EmailService(IOptions<AppSettings> settings)
    {
        _settings = settings.Value;
        _graphClient = CreateGraphClient();
    }

    /// <summary>
    /// Creates Microsoft Graph client using Client Credentials flow.
    /// Returns null if credentials are not configured.
    /// </summary>
    private GraphServiceClient? CreateGraphClient()
    {
        if (string.IsNullOrWhiteSpace(_settings.GraphClientId) || 
            string.IsNullOrWhiteSpace(_settings.GraphClientSecret) || 
            string.IsNullOrWhiteSpace(_settings.GraphTenantId))
        {
            Log.Warning("Microsoft Graph credentials not configured. Email functionality will be disabled.");
            return null;
        }

        var credential = new ClientSecretCredential(
            _settings.GraphTenantId,
            _settings.GraphClientId,
            _settings.GraphClientSecret);

        return new GraphServiceClient(credential);
    }

    /// <summary>
    /// Sends an email with optional Excel attachment via Microsoft Graph.
    /// Saves Excel file to root directory before sending.
    /// </summary>
    /// <param name="request">Email request with recipients, subject, body, and optional attachment</param>
    /// <returns>EmailResponse with success status and message ID</returns>
    public async Task<EmailResponse> SendEmailAsync(EmailRequest request)
    {
        if (_graphClient == null)
        {
            Log.Warning("Email service not configured. Skipping email send.");
            return new EmailResponse
            {
                Success = false,
                Error = "Email service not configured. Microsoft Graph credentials are required."
            };
        }

        try
        {
            // Save Excel file to root directory if attachment is provided
            string? savedFilePath = null;
            if (request.Attachment != null && !string.IsNullOrWhiteSpace(request.AttachmentFileName))
            {
                try
                {
                    // Get root directory (project root)
                    var rootDirectory = Directory.GetCurrentDirectory();
                    var fileName = request.AttachmentFileName;
                    
                    // Ensure .xlsx extension
                    if (!fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                    {
                        fileName = Path.ChangeExtension(fileName, ".xlsx");
                    }

                    savedFilePath = Path.Combine(rootDirectory, fileName);
                    await File.WriteAllBytesAsync(savedFilePath, request.Attachment);
                    
                    Log.Information("Excel file saved to: {FilePath}", savedFilePath);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to save Excel file to disk, but continuing with email send");
                }
            }

            // Validate attachment size
            if (request.Attachment != null && request.Attachment.Length > MaxAttachmentSizeBytes)
            {
                var error = $"Attachment size ({request.Attachment.Length} bytes) exceeds maximum of {MaxAttachmentSizeBytes} bytes";
                Log.Warning(error);
                return new EmailResponse
                {
                    Success = false,
                    Error = error
                };
            }

            // Parse recipients
            var toRecipients = ParseEmailAddresses(request.To);
            if (toRecipients.Count == 0)
            {
                var error = "At least one 'To' recipient is required";
                Log.Warning(error);
                return new EmailResponse
                {
                    Success = false,
                    Error = error
                };
            }

            var ccRecipients = !string.IsNullOrWhiteSpace(request.Cc)
                ? ParseEmailAddresses(request.Cc)
                : new List<Recipient>();

            var bccRecipients = !string.IsNullOrWhiteSpace(request.Bcc)
                ? ParseEmailAddresses(request.Bcc)
                : new List<Recipient>();

            // Build message
            var message = new Message
            {
                Subject = request.Subject,
                Body = new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content = request.Body
                },
                ToRecipients = toRecipients,
                CcRecipients = ccRecipients.Count > 0 ? ccRecipients : null,
                BccRecipients = bccRecipients.Count > 0 ? bccRecipients : null
            };

            // Add attachment if provided
            if (request.Attachment != null && !string.IsNullOrWhiteSpace(request.AttachmentFileName))
            {
                var attachment = new FileAttachment
                {
                    Name = request.AttachmentFileName,
                    ContentBytes = request.Attachment,
                    ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                };

                message.Attachments = new List<Attachment>
                {
                    attachment
                };
            }

            // Send email
            var sendMailRequest = new Microsoft.Graph.Users.Item.SendMail.SendMailPostRequestBody
            {
                Message = message,
                SaveToSentItems = true
            };

            var senderEmail = !string.IsNullOrWhiteSpace(request.From)
                ? request.From
                : _settings.MailFrom ?? throw new InvalidOperationException("MAIL_FROM not configured");

            Log.Information("Sending email to {ToCount} recipient(s), CC: {CcCount}, BCC: {BccCount}, Subject: {Subject}",
                toRecipients.Count, ccRecipients.Count, bccRecipients.Count, request.Subject);

            if (!string.IsNullOrEmpty(savedFilePath))
            {
                Log.Information("Excel file saved at: {FilePath}", savedFilePath);
            }

            await _graphClient.Users[senderEmail].SendMail.PostAsync(sendMailRequest);

            Log.Information("Email sent successfully. Subject: {Subject}, File saved: {FilePath}", 
                request.Subject, savedFilePath ?? "N/A");

            return new EmailResponse
            {
                Success = true,
                MessageId = Guid.NewGuid().ToString() // Graph doesn't return message ID immediately
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to send email. Subject: {Subject}", request.Subject);
            return new EmailResponse
            {
                Success = false,
                Error = $"Failed to send email: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Parses comma-separated email addresses into Recipient list.
    /// </summary>
    private List<Recipient> ParseEmailAddresses(string emailAddresses)
    {
        if (string.IsNullOrWhiteSpace(emailAddresses))
        {
            return new List<Recipient>();
        }

        return emailAddresses
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(email => email.Trim())
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Select(email => new Recipient
            {
                EmailAddress = new EmailAddress
                {
                    Address = email
                }
            })
            .ToList();
    }
}

