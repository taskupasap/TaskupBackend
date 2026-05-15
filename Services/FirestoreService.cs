using Google.Cloud.Firestore;
using Google.Cloud.Firestore.V1;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;

namespace taskup_backend.Services;

public class FirestoreService
{
    private readonly FirestoreDb _db;

    public FirestoreService(IConfiguration config)
    {
        var projectId = config["Firebase:ProjectId"];
        var firebaseJson = config["Firebase:ServiceAccountJson"];
        var firebasePath = config["Firebase:ServiceAccountPath"];

        GoogleCredential credential;

        // 1. Production (Render): Read raw JSON string from Environment Variables
        if (!string.IsNullOrEmpty(firebaseJson))
        {
            credential = GoogleCredential.FromJson(firebaseJson);
        }
        // 2. Local Development: Read from physical file path
        else if (!string.IsNullOrEmpty(firebasePath))
        {
            credential = GoogleCredential.FromFile(firebasePath);
        }
        else
        {
            // Ultimate fallback for local testing
            credential = GoogleCredential.FromFile("firebase-config.json");
        }

        // Build the Firestore Database connection securely
        var builder = new FirestoreDbBuilder
        {
            ProjectId = projectId,
            Credential = credential
        };

        _db = builder.Build();
    }
    // Expose the Collection method so your Controllers can use it
    public CollectionReference Collection(string path)
    {
        return _db.Collection(path);
    }

    //public CollectionReference Collection(string name) => _db.Collection(name);

    // Helper to get a specific document reference
    public DocumentReference Document(string collection, string documentId) => _db.Collection(collection).Document(documentId);
}