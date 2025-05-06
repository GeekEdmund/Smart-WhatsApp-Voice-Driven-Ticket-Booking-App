namespace VoiceToEmail.Core.Models;

public class EventDetails
{
    public string EventName { get; set; } = string.Empty;
    public string RequestedDate { get; set; } = string.Empty;
    public string FanName { get; set; } = string.Empty;
    public string FanEmail { get; set; } = string.Empty;
    public int TicketQuantity { get; set; } = 1;
    public string SpecialRequirements { get; set; } = string.Empty;
    public string TicketType { get; set; } = "Standard";
    public List<string> SeatNumbers { get; set; } = new List<string>();

    // Optional: Helper property to parse the date
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTime? RequestedDateParsed
    {
        get
        {
            if (DateTime.TryParse(RequestedDate, out var dt))
                return dt;
            return null;
        }
    }

    public DateTime GetRequestedDateOrDefault()
    {
        var date = RequestedDateParsed ?? DateTime.Now.AddDays(1);
        return date;
    }

}

public class EventInfo
{
    public DateTime Date { get; set; }
    public int AvailableSeats { get; set; }
    public string Venue { get; set; } = string.Empty;
    public string KickoffTime { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public Dictionary<string, decimal> TicketPrices { get; set; } = new();
    public Queue<string> SeatNumbers { get; set; } = new();
}

public class TicketBookingResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public BookingDetails BookingDetails { get; set; } = new();
}

public class BookingDetails
{
    public string EventName { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Venue { get; set; } = string.Empty;
    public string KickoffTime { get; set; } = string.Empty;
    public string TicketReference { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public string Category { get; set; } = string.Empty;
    public DateTime BookingTime { get; set; } = DateTime.UtcNow;
    public decimal TotalPrice { get; set; }

    public List<string> SeatNumbers { get; set; } = new List<string>();
    public string RequestedDate { get; set; }
}