using Microsoft.AspNetCore.Mvc;
using Google.Cloud.Firestore;
using taskup_backend.Services;

namespace taskup_backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InviteController : ControllerBase
{
    private readonly FirestoreService _firestore;

    public InviteController(FirestoreService firestore)
    {
        _firestore = firestore;
    }

    [HttpGet("{orgId}")]
    public async Task<IActionResult> GetInviteInfo(string orgId)
    {
        var orgRef = _firestore.Collection("organizations").Document(orgId);
        var snapshot = await orgRef.GetSnapshotAsync();

        if (!snapshot.Exists) return NotFound("Organization not found.");

        // Safe check for fields
        string code = snapshot.ContainsField("inviteCode") ? snapshot.GetValue<string>("inviteCode") : "N/A";
        string name = snapshot.ContainsField("name") ? snapshot.GetValue<string>("name") : "Unknown";

        return Ok(new
        {
            InviteCode = code,
            OrgName = name
        });
    }

    [HttpPost("{orgId}/regenerate")]
    public async Task<IActionResult> RegenerateCode(string orgId)
    {
        var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();
        var newCode = new string(Enumerable.Repeat(chars, 6).Select(s => s[random.Next(s.Length)]).ToArray());

        var orgRef = _firestore.Collection("organizations").Document(orgId);
        await orgRef.UpdateAsync("inviteCode", newCode);

        return Ok(new { NewCode = newCode });
    }
    [HttpGet("validate/{code}")]
    public async Task<IActionResult> ValidateCode(string code)
    {
        // Search the organizations collection for the matching code
        // Note: Check your Firestore database to see if the field is 'InviteCode' or 'inviteCode'
        // based on how it was saved during Admin registration.
        var query = _firestore.Collection("organizations").WhereEqualTo("InviteCode", code.ToUpper());
        var snapshot = await query.GetSnapshotAsync();

        if (snapshot.Count == 0)
        {
            // Fallback check just in case it was saved lowercase in Firestore
            query = _firestore.Collection("organizations").WhereEqualTo("inviteCode", code.ToUpper());
            snapshot = await query.GetSnapshotAsync();

            if (snapshot.Count == 0)
                return BadRequest(new { message = "Invalid or expired Invite Code." });
        }

        var orgDoc = snapshot.Documents[0];
        string orgName = orgDoc.ContainsField("name") ? orgDoc.GetValue<string>("name") :
                         orgDoc.ContainsField("Name") ? orgDoc.GetValue<string>("Name") : "Unknown Workspace";

        // Grab the org type (company, college, school)
        string orgType = orgDoc.ContainsField("type") ? orgDoc.GetValue<string>("type") : "company";

        return Ok(new
        {
            OrgId = orgDoc.Id,
            OrgName = orgName,
            OrgType = orgType // <-- Added this
        });
    }
}