using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using taskup_backend.Controllers;
using taskup_backend.Middleware;
using taskup_backend.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins("http://localhost:4200") // Your frontend URL
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// Register FirestoreService as a Singleton
builder.Services.AddSingleton<FirestoreService>();
builder.Services.AddSingleton<CloudinaryService>();
// Initialize Firebase Admin SDK Safely
var firebaseConfigPath = builder.Configuration["Firebase:ServiceAccountPath"] ?? "firebase-config.json";

if (File.Exists(firebaseConfigPath))
{
    // The "Ghost" check: only create if it doesn't exist yet
    if (FirebaseAdmin.FirebaseApp.DefaultInstance == null)
    {
        FirebaseAdmin.FirebaseApp.Create(new FirebaseAdmin.AppOptions()
        {
            Credential = Google.Apis.Auth.OAuth2.GoogleCredential.FromFile(firebaseConfigPath)
        });
    }
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAngular");

// 3. Custom Firebase Auth Middleware
app.UseMiddleware<FirebaseAuthMiddleware>();

app.UseAuthorization();
app.MapControllers();

app.Run();