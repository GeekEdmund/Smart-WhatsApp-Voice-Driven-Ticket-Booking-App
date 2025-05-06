# Smart WhatsApp Voice-Driven Ticket Booking App

A .NET-based API that enables users to book tickets via WhatsApp voice messages. The app leverages OpenAI for natural language processing, AssemblyAI for speech-to-text, Twilio for WhatsApp integration, and SendGrid for email notifications.

---

## Features

- **WhatsApp Integration:** Receive and respond to user messages via WhatsApp using Twilio.
- **Voice Recognition:** Convert WhatsApp voice messages to text using AssemblyAI.
- **AI-Powered Understanding:** Use OpenAI to interpret user intent and extract booking details.
- **Email Notifications:** Send booking confirmations and generated tickets via SendGrid.
- **Secure & Configurable:** All API keys and sensitive data are managed via configuration.

---

## Technologies Used

- [.NET 9.0](https://dotnet.microsoft.com/)
- [Twilio API](https://www.twilio.com/)
- [AssemblyAI API](https://www.assemblyai.com/)
- [OpenAI API](https://platform.openai.com/)
- [SendGrid API](https://sendgrid.com/)

---

## Getting Started

### Prerequisites

- [.NET SDK 9.0+](https://dotnet.microsoft.com/download)
- [Git](https://git-scm.com/)
- Accounts and API keys for Twilio, AssemblyAI, OpenAI, and SendGrid

### Setup

1. **Clone the repository:**
   ```sh
   git clone https://github.com/GeekEdmund/Smart-WhatsApp-Voice-Driven-Ticket-Booking-App.git
   cd Smart-WhatsApp-Voice-Driven-Ticket-Booking-App
   ```

2. **Configure environment:**
   - Copy `appsettings.json` and replace all API keys and emails with your own credentials.
   - **Do not commit real secrets to the repository.**

3. **Restore dependencies:**
   ```sh
   dotnet restore
   ```

4. **Build and run the project:**
   ```sh
   dotnet build
   dotnet run --project VoiceToEmail.API
   ```

---

## Usage

- Send a voice message to the configured WhatsApp number.
- The app will transcribe, process, and respond with booking details.
- Confirmation is sent via email.

---

## Configuration

All configuration is managed in `appsettings.json`:

```json
{
  "OpenAI": {
    "ApiKey": "YOUR_OPENAI_API_KEY"
  },
  "SendGrid": {
    "ApiKey": "YOUR_SENDGRID_API_KEY",
    "FromEmail": "your@email.com",
    "FromName": "Your Name"
  },
  "Twilio": {
    "AccountSid": "YOUR_TWILIO_ACCOUNT_SID",
    "AuthToken": "YOUR_TWILIO_AUTH_TOKEN",
    "WhatsAppNumber": "+1234567890"
  },
  "AssemblyAI": {
    "ApiKey": "YOUR_ASSEMBLYAI_API_KEY"
  },
  "AllowedHosts": "*"
}
```

**Note:** Never commit real API keys or secrets to your repository.

---

## Contributing

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/YourFeature`)
3. Commit your changes (`git commit -am 'Add some feature'`)
4. Push to the branch (`git push origin feature/YourFeature`)
5. Open a pull request

---

## License

This project is licensed under the MIT License.

---

## Acknowledgements

- [Twilio](https://www.twilio.com/)
- [AssemblyAI](https://www.assemblyai.com/)
- [OpenAI](https://platform.openai.com/)
- [SendGrid](https://sendgrid.com/)
