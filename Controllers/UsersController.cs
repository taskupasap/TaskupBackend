using Microsoft.AspNetCore.Mvc;
using Google.Cloud.Firestore;
using taskup_backend.Services; // <-- Added this to access our custom service

namespace taskup_backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly FirestoreService _firestore; // <-- Swapped to FirestoreService

    // <-- Swapped to FirestoreService
    public UsersController(FirestoreService firestore)
    {
        _firestore = firestore;
    }

    [HttpGet("org/{orgId}/leaderboard")]
    public async Task<IActionResult> GetLeaderboard(string orgId)
    {
        var usersRef = _firestore.Collection("users");

        // 1. Only query by orgId to bypass strict Firestore Index requirements
        var query = usersRef.WhereEqualTo("orgId", orgId);
        var snapshot = await query.GetSnapshotAsync();

        // 2. Map with explicit lowercase keys so Angular can read them perfectly
        var leaderboard = snapshot.Documents.Select(doc => new {
            id = doc.Id,
            displayName = doc.ContainsField("displayName") ? doc.GetValue<string>("displayName") : "Unknown User",
            xp = doc.ContainsField("xp") ? doc.GetValue<int>("xp") : 0,
            level = doc.ContainsField("level") ? doc.GetValue<int>("level") : 1,
            role = doc.ContainsField("role") ? doc.GetValue<string>("role") : "member"
        })
        .OrderByDescending(u => u.xp) // 3. Sort in-memory instead of in the database
        .Take(50)
        .ToList();

        return Ok(leaderboard);
    }
}