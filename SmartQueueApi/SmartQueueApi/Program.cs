using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

var dbPath = builder.Configuration["DB_PATH"] ?? "queue.db";
var connStr = $"Data Source={dbPath};Default Timeout=5";

using (var conn = new SqliteConnection(connStr))
{
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        PRAGMA journal_mode = WAL;
        CREATE TABLE IF NOT EXISTS customers (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            name       TEXT NOT NULL,
            status     TEXT NOT NULL DEFAULT 'Waiting'
                       CHECK (status IN ('Waiting', 'Serving', 'Completed')),
            created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
        );
        CREATE INDEX IF NOT EXISTS idx_customers_status_id ON customers (status, id);
        """;
    cmd.ExecuteNonQuery();
}


SqliteConnection Open()
{
    var c = new SqliteConnection(connStr);
    c.Open();
    return c;
}

CustomerDto Map(SqliteDataReader r, long? position = null)
{
    var status = r.GetString(2);
    return new CustomerDto(
        r.GetInt64(0),
        r.GetString(1),
        status,
        r.GetString(3),
        status == "Waiting" ? position : null);
}

long GetPosition(SqliteConnection c, long id)
{
    using var cmd = c.CreateCommand();
    cmd.CommandText = "SELECT COUNT(*) FROM customers WHERE status = 'Waiting' AND id <= $id";
    cmd.Parameters.AddWithValue("$id", id);
    return (long)cmd.ExecuteScalar()!;
}


app.MapPost("/api/queue", (CreateRequest req) =>
{
    var name = req.Name?.Trim();
    if (string.IsNullOrEmpty(name) || name.Length > 100)
        return Results.BadRequest(new { error = "name is required (1-100 characters)" });

    using var c = Open();
    using var cmd = c.CreateCommand();
    cmd.CommandText = """
        INSERT INTO customers (name) VALUES ($name)
        RETURNING id, name, status, created_at
        """;
    cmd.Parameters.AddWithValue("$name", name);
    using var r = cmd.ExecuteReader();
    r.Read();
    var id = r.GetInt64(0);
    var dto = Map(r);
    r.Close();

    dto = dto with { Position = GetPosition(c, id) };
    return Results.Created($"/api/queue/{id}", dto);
});

app.MapGet("/api/queue", () =>
{
    using var c = Open();
    using var cmd = c.CreateCommand();
    cmd.CommandText = """
        SELECT id, name, status, created_at
        FROM customers WHERE status = 'Waiting' ORDER BY id
        """;
    using var r = cmd.ExecuteReader();
    var list = new List<CustomerDto>();
    long pos = 1;
    while (r.Read()) list.Add(Map(r, pos++));
    return Results.Ok(list);
});


app.MapPost("/api/queue/next", () =>
{
    using var c = Open();
    using var cmd = c.CreateCommand();
    cmd.CommandText = """
        UPDATE customers
        SET status = 'Serving'
        WHERE status = 'Waiting'
          AND id = (
              SELECT id FROM customers
              WHERE status = 'Waiting'
              ORDER BY id
              LIMIT 1
          )
        RETURNING id, name, status, created_at
        """;
    using var r = cmd.ExecuteReader();
    if (!r.Read())
        return Results.NotFound(new { error = "No customers waiting" });
    return Results.Ok(Map(r));
});


app.MapGet("/api/queue/{id:long}", (long id) =>
{
    using var c = Open();
    using var cmd = c.CreateCommand();
    cmd.CommandText = "SELECT id, name, status, created_at FROM customers WHERE id = $id";
    cmd.Parameters.AddWithValue("$id", id);
    using var r = cmd.ExecuteReader();
    if (!r.Read())
        return Results.NotFound(new { error = "Customer not found" });
    var dto = Map(r);
    r.Close();
    return Results.Ok(dto with { Position = dto.Status == "Waiting" ? GetPosition(c, id) : null });
});


app.MapPost("/api/queue/{id:long}/complete", (long id) =>
{
    using var c = Open();
    using var cmd = c.CreateCommand();
    cmd.CommandText = """
        UPDATE customers SET status = 'Completed'
        WHERE id = $id AND status = 'Serving'
        RETURNING id, name, status, created_at
        """;
    cmd.Parameters.AddWithValue("$id", id);
    using var r = cmd.ExecuteReader();
    if (!r.Read())
        return Results.NotFound(new { error = "Customer not found or not in Serving status" });
    return Results.Ok(Map(r));
});


app.MapDelete("/api/queue/{id:long}", (long id) =>
{
    using var c = Open();
    using var cmd = c.CreateCommand();
    cmd.CommandText = "DELETE FROM customers WHERE id = $id";
    cmd.Parameters.AddWithValue("$id", id);
    return cmd.ExecuteNonQuery() == 0
        ? Results.NotFound(new { error = "Customer not found" })
        : Results.NoContent();
});

app.Run();

record CreateRequest(string? Name);
record CustomerDto(long Id, string Name, string Status, string CreatedAt, long? Position);