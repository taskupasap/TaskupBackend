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
        // 🚨 THE FIX: Filter out any user who has the 'admin' role!
        .Where(u => u.role.ToLower() != "admin")
        .OrderByDescending(u => u.xp) // 3. Sort in-memory instead of in the database
        .Take(50)
        .ToList();

        return Ok(leaderboard);
    }

    [HttpPost("join-workspace")]
    public async Task<IActionResult> JoinWorkspace([FromBody] Dictionary<string, string> request)
    {
        // 1. Get the logged-in user
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized("User not logged in.");

        if (!request.ContainsKey("orgId") || string.IsNullOrEmpty(request["orgId"]))
            return BadRequest("Organization ID is required.");

        string orgIdToJoin = request["orgId"].Trim();

        // 2. (Optional but recommended) Verify the Org actually exists!
        // var orgRef = _firestore.Collection("organizations").Document(orgIdToJoin);
        // var orgSnap = await orgRef.GetSnapshotAsync();
        // if (!orgSnap.Exists) return NotFound("Workspace not found. Check your invite code.");

        // 3. Update the User's profile with their new Workspace ID
        var userRef = _firestore.Collection("users").Document(userId);

        var updates = new Dictionary<string, object>
        {
            { "orgId", orgIdToJoin },
            { "workspaceType", "company" } // Defaulting to company, could be dynamically pulled from the org document
        };

        await userRef.UpdateAsync(updates);

        return Ok(new { message = "Successfully joined the workspace!", orgId = orgIdToJoin });
    }
}