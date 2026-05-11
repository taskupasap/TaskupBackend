using Microsoft.AspNetCore.Mvc;
using Google.Cloud.Firestore;
using taskup_backend.Models;
using taskup_backend.Services;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace taskup_backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly FirestoreService _firestore;

    public DashboardController(FirestoreService firestore)
    {
        _firestore = firestore;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats([FromQuery] string orgId)
    {
        try
        {
            // 1. Get Task Stats
            var tasksQuery = _firestore.Collection("tasks").WhereEqualTo("OrgId", orgId);
            var tasksSnap = await tasksQuery.GetSnapshotAsync();

            // 2. Get Member Stats
            var membersQuery = _firestore.Collection("users").WhereEqualTo("OrgId", orgId);
            var membersSnap = await membersQuery.GetSnapshotAsync();

            var stats = new
            {
                TotalTasks = tasksSnap.Count,
                CompletedTasks = tasksSnap.Documents.Count(d => d.GetValue<string>("Status") == "completed"),
                TotalMembers = membersSnap.Count,
                ActiveDeadlines = tasksSnap.Documents.Count(d => d.GetValue<DateTime>("Deadline") > DateTime.UtcNow)
            };

            return Ok(stats);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }
}