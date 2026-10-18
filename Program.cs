using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<Store>();
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

const string RA = "Room Assistant", HK = "Housekeeper";

// PROTOTYPE AUTH: role comes from the X-Role header so the three GUIs can be demoed.
// A header is NOT secure. Replace with cookie/JWT authentication and authorization policies
// (e.g. [Authorize(Roles = "Admin")]) before any real deployment. Authorization stays server-side.
static bool Can(HttpContext c, params string[] roles)
{
    var r = c.Request.Headers["X-Role"].ToString();
    return r == "Admin" || roles.Contains(r);
}
static IResult Deny() => Results.Json(new { error = "Not permitted for this role" }, statusCode: 403);
static IResult Bad(string m) => Results.Json(new { error = m }, statusCode: 400);

var api = app.MapGroup("/api");

// Public, read-only: no notices, staff names or internal fields.
api.MapGet("/public/rooms", (Store s) =>
{
    lock (s.Lock) return Results.Ok(s.Rooms.Select(r => new { r.RoomNumber, r.RoomType, r.Status, r.Availability }).ToList());
});

api.MapGet("/rooms", (HttpContext c, Store s) =>
{
    if (!Can(c, RA, HK)) return Deny();
    lock (s.Lock) return Results.Ok(s.Rooms.ToList());
});

api.MapPost("/rooms", (HttpContext c, Store s, RoomEdit b) =>
{
    if (!Can(c)) return Deny();
    if (string.IsNullOrWhiteSpace(b.RoomNumber)) return Bad("Room number is required.");
    lock (s.Lock)
    {
        if (s.Rooms.Any(r => r.RoomNumber == b.RoomNumber.Trim())) return Bad("Room number already exists.");
        var room = new Room { RoomNumber = b.RoomNumber.Trim(), RoomType = b.RoomType, ServiceStatus = b.ServiceStatus };
        s.Rooms.Add(room);
        return Results.Created($"/api/rooms/{room.RoomNumber}", room);
    }
});

api.MapPut("/rooms/{number}", (HttpContext c, Store s, string number, RoomEdit b) =>
{
    if (!Can(c)) return Deny();
    if (string.IsNullOrWhiteSpace(b.RoomNumber)) return Bad("Room number is required.");
    lock (s.Lock)
    {
        var room = s.Rooms.FirstOrDefault(r => r.RoomNumber == number);
        if (room is null) return Results.NotFound();
        var nn = b.RoomNumber.Trim();
        if (nn != number && s.Rooms.Any(r => r.RoomNumber == nn)) return Bad("Room number already exists.");
        s.Notices.Where(n => n.RoomNumber == number).ToList().ForEach(n => n.RoomNumber = nn);
        room.RoomNumber = nn; room.RoomType = b.RoomType; room.ServiceStatus = b.ServiceStatus;
        room.LastUpdated = DateTime.Now;
        return Results.Ok(room);
    }
});

api.MapDelete("/rooms/{number}", (HttpContext c, Store s, string number) =>
{
    if (!Can(c)) return Deny();
    lock (s.Lock) return s.Rooms.RemoveAll(r => r.RoomNumber == number) > 0 ? Results.NoContent() : Results.NotFound();
});

// Occupancy / housekeeping updates (Room Assistant + Admin).
api.MapPatch("/rooms/{number}/status", (HttpContext c, Store s, string number, StatusPatch b) =>
{
    if (!Can(c, RA)) return Deny();
    lock (s.Lock)
    {
        var room = s.Rooms.FirstOrDefault(r => r.RoomNumber == number);
        if (room is null) return Results.NotFound();
        if (b.OccupancyStatus is "Occupied" or "Vacant") room.OccupancyStatus = b.OccupancyStatus;
        if (b.HousekeepingStatus is "Clean" or "Dirty") room.HousekeepingStatus = b.HousekeepingStatus;
        room.LastUpdated = DateTime.Now;
        return Results.Ok(room);
    }
});

api.MapGet("/notices", (HttpContext c, Store s) =>
{
    if (!Can(c, RA, HK)) return Deny();
    lock (s.Lock) return Results.Ok(s.Notices.OrderByDescending(n => n.CreatedAt).ToList());
});

api.MapPost("/notices", (HttpContext c, Store s, NewNotice b) =>
{
    if (!Can(c, RA)) return Deny();
    if (string.IsNullOrWhiteSpace(b.Message) || b.Message.Length > 300) return Bad("Message is required (max 300 characters).");
    lock (s.Lock)
    {
        var room = s.Rooms.FirstOrDefault(r => r.RoomNumber == b.RoomNumber);
        if (room is null) return Bad("Unknown room.");
        var n = new Notice { Id = s.Notices.Count == 0 ? 1 : s.Notices.Max(x => x.Id) + 1, RoomNumber = b.RoomNumber, Message = b.Message.Trim(),
            CreatedBy = c.Request.Headers["X-Role"].ToString() == "Admin" ? "Admin" : RA, CreatedAt = DateTime.Now };
        s.Notices.Add(n);
        room.OccupancyStatus = "Vacant"; room.HousekeepingStatus = "Dirty"; room.LastUpdated = DateTime.Now;
        return Results.Created($"/api/notices/{n.Id}", n);
    }
});

// Pending -> Acknowledged -> Cleaning -> Completed, one step at a time.
api.MapPatch("/notices/{id:int}", (HttpContext c, Store s, int id, NoticePatch b) =>
{
    if (!Can(c, HK)) return Deny();
    string[] flow = { "Pending", "Acknowledged", "Cleaning", "Completed" };
    lock (s.Lock)
    {
        var n = s.Notices.FirstOrDefault(x => x.Id == id);
        if (n is null) return Results.NotFound();
        if (Array.IndexOf(flow, b.Status) != Array.IndexOf(flow, n.Status) + 1) return Bad("Invalid status change.");
        n.Status = b.Status;
        var room = s.Rooms.FirstOrDefault(r => r.RoomNumber == n.RoomNumber);
        if (room is not null && b.Status is "Cleaning" or "Completed")
        {
            room.HousekeepingStatus = b.Status == "Cleaning" ? "Cleaning" : "Clean";
            room.LastUpdated = DateTime.Now;
        }
        return Results.Ok(n);
    }
});

app.Run();

record RoomEdit(string RoomNumber, string RoomType, string ServiceStatus);
record StatusPatch(string? OccupancyStatus, string? HousekeepingStatus);
record NewNotice(string RoomNumber, string Message);
record NoticePatch(string Status);

class Room
{
    public string RoomNumber { get; set; } = "";
    public string RoomType { get; set; } = "Standard";
    public string OccupancyStatus { get; set; } = "Vacant";      // Vacant | Occupied
    public string HousekeepingStatus { get; set; } = "Clean";    // Clean | Dirty | Cleaning
    public string ServiceStatus { get; set; } = "In Service";    // In Service | Out of Service
    public DateTime LastUpdated { get; set; } = DateTime.Now;
    public string Status => ServiceStatus != "In Service" ? "Out of Service"
        : OccupancyStatus == "Occupied" ? "Occupied"
        : HousekeepingStatus == "Dirty" ? "Dirty"
        : HousekeepingStatus == "Cleaning" ? "Cleaning" : "Available";
    public string Availability => Status == "Available" ? "Available" : "Unavailable";
}

class Notice
{
    public int Id { get; set; }
    public string RoomNumber { get; set; } = "";
    public string Message { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string CreatedBy { get; set; } = "Room Assistant";
    public DateTime CreatedAt { get; set; }
}

// Prototype store seeded from SampleData/*.json, held in memory.
// Replace with EF Core + a real database for multi-user deployments.
class Store
{
    public readonly object Lock = new();
    public List<Room> Rooms;
    public List<Notice> Notices;
    public Store()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        string P(string f) => Path.Combine(AppContext.BaseDirectory, "SampleData", f);
        Rooms = JsonSerializer.Deserialize<List<Room>>(File.ReadAllText(P("rooms.json")), o) ?? new();
        Notices = JsonSerializer.Deserialize<List<Notice>>(File.ReadAllText(P("notices.json")), o) ?? new();
    }
}
