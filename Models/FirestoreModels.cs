using Google.Cloud.Firestore;
using System;

namespace taskup_backend.Models;

[FirestoreData]
public class TaskItem
{
    // FirestoreDocumentId automatically grabs the document's unique ID
    [FirestoreDocumentId]
    public string Id { get; set; } = "";

    [FirestoreProperty("title")]
    public string Title { get; set; } = "";

    // Added description based on your Firebase document
    [FirestoreProperty("description")]
    public string Description { get; set; } = "";

    [FirestoreProperty("status")]
    public string Status { get; set; } = "";

    [FirestoreProperty("orgId")]
    public string OrgId { get; set; } = "";

    [FirestoreProperty("priority")]
    public string Priority { get; set; } = "";

    [FirestoreProperty("type")]
    public string Type { get; set; } = "";

    [FirestoreProperty("xpReward")]
    public int XPReward { get; set; } = 0;

    [FirestoreProperty("deadline")]
    public DateTime Deadline { get; set; }

    [FirestoreProperty("createdAt")]
    public DateTime CreatedAt { get; set; }

    [FirestoreProperty("assignedTo")]
    public List<string> AssignedTo { get; set; }
}