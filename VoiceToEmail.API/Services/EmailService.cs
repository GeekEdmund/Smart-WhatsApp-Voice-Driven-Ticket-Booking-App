using SendGrid;
using SendGrid.Helpers.Mail;
using VoiceToEmail.Core.Interfaces;
using VoiceToEmail.Core.Models;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using System.IO;

public class EmailService : IEmailService
{
    private readonly SendGridClient _client;
    private readonly string _fromEmail;
    private readonly string _fromName;
    
    public EmailService(IConfiguration configuration)
    {
        var apiKey = configuration["SendGrid:ApiKey"] ?? 
            throw new ArgumentNullException("SendGrid:ApiKey configuration is missing");
        _client = new SendGridClient(apiKey);
        
        _fromEmail = configuration["SendGrid:FromEmail"] ?? 
            throw new ArgumentNullException("SendGrid:FromEmail configuration is missing");
        _fromName = configuration["SendGrid:FromName"] ?? 
            throw new ArgumentNullException("SendGrid:FromName configuration is missing");
    }
    
    public async Task SendEmailAsync(string to, string subject, string content)
    {
        await SendEmailAsync(to, subject, content, null);
    }
    
    public async Task SendEmailAsync(string to, string subject, string content, byte[]? pdfAttachment = null)
    {
        var from = new EmailAddress(_fromEmail, _fromName);
        var toAddress = new EmailAddress(to);
        
        var msg = MailHelper.CreateSingleEmail(
            from,
            toAddress,
            subject,
            content,
            $"<div style='font-family: Arial, sans-serif;'>{content}</div>"
        );
        
        // Attach PDF if provided
        if (pdfAttachment != null)
        {
            msg.AddAttachment("Ticket.pdf", Convert.ToBase64String(pdfAttachment), "application/pdf");
        }
        
        var response = await _client.SendEmailAsync(msg);
        
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to send email: {response.StatusCode}");
        }
    }
    
    private byte[] GenerateSoccerTicketPdf(object bookingDetails)
    {
        var details = bookingDetails as BookingDetails;
        if (details == null)
            throw new ArgumentException("bookingDetails must be of type BookingDetails");

        using (var doc = new PdfDocument())
        {
            var page = doc.AddPage();
            page.Width = XUnit.FromMillimeter(170);
            page.Height = XUnit.FromMillimeter(70);

            var gfx = XGraphics.FromPdfPage(page);

            // Draw border
            gfx.DrawRectangle(XPens.Black, 5, 5, page.Width - 10, page.Height - 10);

            // Main Title
            var titleFont = new XFont("Arial", 18, XFontStyle.Bold);
            gfx.DrawString("SOCCER MATCH TICKET", titleFont, XBrushes.DarkBlue, new XRect(0, 15, page.Width, 30), XStringFormats.TopCenter);

            // Event Info
            var font = new XFont("Arial", 12, XFontStyle.Regular);
            gfx.DrawString($"Match: {details.EventName}", font, XBrushes.Black, new XRect(20, 45, page.Width - 40, 20), XStringFormats.TopLeft);
            gfx.DrawString($"Date: {details.Date:dd MMM yyyy}", font, XBrushes.Black, new XRect(20, 60, 200, 20), XStringFormats.TopLeft);
            gfx.DrawString($"Kickoff: {details.KickoffTime}", font, XBrushes.Black, new XRect(220, 60, 200, 20), XStringFormats.TopLeft);
            gfx.DrawString($"Venue: {details.Venue}", font, XBrushes.Black, new XRect(20, 75, page.Width - 40, 20), XStringFormats.TopLeft);
            gfx.DrawString($"Category: {details.Category}", font, XBrushes.Black, new XRect(20, 90, 200, 20), XStringFormats.TopLeft);
            gfx.DrawString($"Quantity: {details.Quantity}", font, XBrushes.Black, new XRect(220, 90, 200, 20), XStringFormats.TopLeft);
            gfx.DrawString($"Ticket Ref: {details.TicketReference}", font, XBrushes.Black, new XRect(20, 105, page.Width - 40, 20), XStringFormats.TopLeft);
            gfx.DrawString($"Issued to: {details.UserEmail}", font, XBrushes.Black, new XRect(20, 120, page.Width - 40, 20), XStringFormats.TopLeft);

            // Add seat numbers
            var seatNumbers = details.SeatNumbers != null ? string.Join(", ", details.SeatNumbers) : "Unassigned";
            gfx.DrawString($"Seats: {seatNumbers}", font, XBrushes.Black, new XRect(20, 135, page.Width - 40, 20), XStringFormats.TopLeft);

            // Add amount paid
            gfx.DrawString($"Amount Paid: £{details.TotalPrice:F2}", font, XBrushes.Black, new XRect(20, 150, page.Width - 40, 20), XStringFormats.TopLeft);

            // Fake barcode (for demo)
            var barcodeFont = new XFont("Arial", 20, XFontStyle.Bold);
            gfx.DrawString("|| ||| | || |||", barcodeFont, XBrushes.Black, new XRect(20, 170, page.Width - 40, 30), XStringFormats.TopLeft);

            // Save to byte array
            using (var ms = new MemoryStream())
            {
                doc.Save(ms, false);
                return ms.ToArray();
            }
        }
    }
    
    public async Task SendSoccerTicketEmailAsync(string userEmail, string subject, string body, BookingDetails bookingDetails)
    {
        // Enhance email body with seat numbers and amount paid
        var seatNumbers = bookingDetails.SeatNumbers != null ? string.Join(", ", bookingDetails.SeatNumbers) : "Unassigned";
        var enhancedBody = $"{body}<br/><br/><strong>Seats:</strong> {seatNumbers}<br/><strong>Amount Paid:</strong> £{bookingDetails.TotalPrice:F2}";

        var pdfBytes = GenerateSoccerTicketPdf(bookingDetails);

        await SendEmailAsync(userEmail, subject, enhancedBody, pdfBytes);
    }

    public async Task SendSoccerTicketEmailAsync(string userEmail, string subject, string body, object bookingDetails)
    {
        if (bookingDetails is BookingDetails details)
        {
            await SendSoccerTicketEmailAsync(userEmail, subject, body, details);
        }
        else
        {
            throw new ArgumentException("bookingDetails must be of type BookingDetails");
        }
    }
}