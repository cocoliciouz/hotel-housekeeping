# Hotel Housekeeping Room Status and Availability System

ASP.NET Core (.NET 8) minimal API + static HTML/CSS/JS, Neomorphic (Soft UI).

## Run
    dotnet run
Open the URL printed in the console, then:

| Page | URL |
|---|---|
| Public browsing (no login, read-only) | `/` |
| Room Assistant | `/assistant.html` |
| Housekeeper | `/housekeeper.html` |
| Admin (has buttons to open the other two) | `/admin.html` |

## Structure
- `Program.cs` – JSON API, server-side role checks, in-memory store.
- `SampleData/*.json` – seed data only (rooms, notices); not a database.
- `wwwroot/css/neo.css` – design tokens (CSS variables) and components.
- `wwwroot/js/common.js` – shared API client, badges, summary, filters, toast, confirm dialog.

## Prototype vs production
- Roles are sent as an `X-Role` header so the GUIs can be demoed. This is **not secure**.
  Replace it with cookie/JWT authentication and role policies; the endpoints already check the role on the server.
- Data lives in memory and resets on restart. Swap `Store` for EF Core + a real database for multi-user use.
- Notice flow: Pending → Acknowledged → Cleaning → Completed; room status follows (Cleaning, then Available).
- The public endpoint `/api/public/rooms` returns only room number, type, status and availability.
