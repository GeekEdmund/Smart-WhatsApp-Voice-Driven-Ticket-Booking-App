using Microsoft.Extensions.DependencyInjection;
using VoiceToEmail.API.Services;
using VoiceToEmail.Core.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register HttpClient
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<IAIAgentService, AIAgentService>();

// Register services
builder.Services.AddScoped<ITranscriptionService, TranscriptionService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<ITicketService, TicketService>();
builder.Services.AddScoped<IAIAgentService, AIAgentService>();
builder.Services.AddScoped<IWhatsAppService, WhatsAppService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();

// Use top-level route registration (fixing ASP0014 warning)
app.MapControllers();

app.Run();