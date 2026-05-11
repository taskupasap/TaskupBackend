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
        string projectId = config["Firebase:ProjectId"] ?? throw new Exception("Firebase ProjectId missing in appsettings");
        string configPath = config["Firebase:ServiceAccountPath"] ?? "firebase-config.json";

        // Build the Firestore client using the explicit service account file
        FirestoreDbBuilder builder = new FirestoreDbBuilder
        {
            ProjectId = projectId,
            Credential = GoogleCredential.FromFile(configPath)
        };

        _db = builder.Build();
    }

    public CollectionReference Collection(string name) => _db.Collection(name);

    // Helper to get a specific document reference
    public DocumentReference Document(string collection, string documentId) => _db.Collection(collection).Document(documentId);
}