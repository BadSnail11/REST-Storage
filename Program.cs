var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
var app = builder.Build();

app.UseHttpsRedirection();
app.UseStatusCodePages();

string storageRoot = Path.Combine(Directory.GetCurrentDirectory(), "Storage");
Directory.CreateDirectory(storageRoot);

app.MapPut("/{**path}", async (HttpContext context) =>
{
    var (path, error) = GetSafePath(context.Request.Path, storageRoot);
    if (error != null) return error;

    Directory.CreateDirectory(Path.GetDirectoryName(path)!);

    await using var fileStream = File.Create(path);
    await context.Request.Body.CopyToAsync(fileStream);

    return Results.Created();
}).Accepts<IFormFile>("application/octet-stream");

app.MapGet("/{**path}", async (HttpContext context) =>
{
    var (path, error) = GetSafePath(context.Request.Path, storageRoot);
    if (error != null) return error;

    if (File.Exists(path))
    {
        return Results.File(path);
    }

    if (Directory.Exists(path))
    {
        var files = Directory.GetFiles(path).Select(f => new
        {
            Name = Path.GetFileName(f),
            Size = new FileInfo(f).Length,
            Modified = File.GetLastWriteTimeUtc(f)
        });

        var dirs = Directory.GetDirectories(path).Select(d => new
        {
            Name = Path.GetFileName(d),
            Modified = Directory.GetLastWriteTimeUtc(d)
        });

        return Results.Ok(new { Files = files, Directories = dirs });
    }

    return Results.NotFound();
});

app.MapMethods("/{**path}", new[] { "HEAD" }, (HttpContext context) =>
{
    var (path, error) = GetSafePath(context.Request.Path, storageRoot);
    if (error != null) return error;

    if (!File.Exists(path)) return Results.NotFound();

    var fileInfo = new FileInfo(path);
    context.Response.Headers.ContentLength = fileInfo.Length;
    context.Response.Headers.LastModified = fileInfo.LastWriteTimeUtc.ToString("R");

    return Results.Ok();
});

app.MapDelete("/{**path}", (HttpContext context) =>
{
    var (path, error) = GetSafePath(context.Request.Path, storageRoot);
    if (error != null) return error;

    if (File.Exists(path))
    {
        File.Delete(path);
        return Results.NoContent();
    }

    if (Directory.Exists(path))
    {
        if (Directory.EnumerateFileSystemEntries(path).Any())
            return Results.Conflict("Directory is not empty");

        Directory.Delete(path);
        return Results.NoContent();
    }

    return Results.NotFound();
});

(string Path, IResult? Error) GetSafePath(PathString requestPath, string rootPath)
{
    try
    {
        var relativePath = requestPath.ToString().TrimStart('/');
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));

        if (!fullPath.StartsWith(rootPath))
            return (null!, Results.BadRequest("Invalid path"));

        return (fullPath, null);
    }
    catch
    {
        return (null!, Results.BadRequest("Invalid path"));
    }
}

app.Run();