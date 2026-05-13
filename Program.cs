using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using taskup_backend.Controllers;
using taskup_backend.Middleware;
using taskup_backend.Services;

var builder = WebApplication.CreateBuilder(args);

var frontendUrl = builder.Environment.IsDevelopment()
    ? "http://localhost:4200"
    : "https://your-app-name.web.app"; // <-- You will get this URL in Step 4
builder.Services.AddCors(options =>
{
    options.AddPolicy("StrictPolicy", policy =>
    {
        policy.WithOrigins(frontendUrl)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // Required if using secure cookies/tokens
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
app.UseCors("StrictPolicy"); // Apply the lock!

// 3. Custom Firebase Auth Middleware
app.UseMiddleware<FirebaseAuthMiddleware>();

app.UseAuthorization();
app.MapControllers();

app.Run();