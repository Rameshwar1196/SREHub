using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aras.SreHub.Auth;
using Aras.SreHub.Learning;
using Aras.SreHub.Models;
using Aras.SreHub.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

if (args.Length > 0 && args[0].Equals("hash-password", StringComparison.OrdinalIgnoreCase))
{
    Console.Write("Password: ");
    var entered = ReadHidden();
    Console.WriteLine();

    if (string.IsNullOrWhiteSpace(entered))
    {
        Console.Error.WriteLine("No password entered.");
        return 1;
    }

    Console.WriteLine(PasswordHasher.Hash(entered));
    return 0;
}

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

if (args.Length > 0 && args[0].Equals("set-password", StringComparison.OrdinalIgnoreCase))
{
    return SetPassword(builder.Environment.ContentRootPath, args.Length > 1 ? args[1] : null);
}

var learningOptions = builder.Configuration.GetSection(LearningOptions.SectionName).Get<LearningOptions>() ?? new LearningOptions();
learningOptions.Normalize(builder.Environment.ContentRootPath);

var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

var repositoryOptions = builder.Configuration.GetSection(RepositoryOptions.SectionName).Get<RepositoryOptions>() ?? new RepositoryOptions();
repositoryOptions.Normalize(builder.Environment.ContentRootPath);

var terminalOptions = builder.Configuration.GetSection(TerminalOptions.SectionName).Get<TerminalOptions>() ?? new TerminalOptions();
terminalOptions.Normalize(builder.Environment.ContentRootPath);

var mathsOptions = builder.Configuration.GetSection(MathsOptions.SectionName).Get<MathsOptions>() ?? new MathsOptions();
mathsOptions.Normalize(builder.Environment.ContentRootPath);

builder.Services.AddSingleton(learningOptions);
builder.Services.AddSingleton(authOptions);
builder.Services.AddSingleton(repositoryOptions);
builder.Services.AddSingleton(terminalOptions);
builder.Services.AddSingleton(mathsOptions);
builder.Services.AddSingleton<MathsLibraryService>();

var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("SreHub")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys")));

if (OperatingSystem.IsWindows())
{
    dataProtection.ProtectKeysWithDpapi();
}
builder.Services.AddSingleton<TerminalService>();
builder.Services.AddSingleton<RepositoryService>();
builder.Services.AddSingleton<WorkLogService>();
builder.Services.AddSingleton<DutyLogService>();
builder.Services.AddSingleton<ImportantLinkService>();
builder.Services.AddSingleton<TrackedWorkService>();
builder.Services.AddSingleton(builder.Configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions());
builder.Services.AddHttpClient("ai", client => client.Timeout = TimeSpan.FromSeconds(90));
builder.Services.AddSingleton<AssistantService>();
builder.Services.AddSingleton<ImportantFileService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddKeyedSingleton("operations", (provider, _) => new SecureFileService(
    provider.GetRequiredService<IWebHostEnvironment>(), provider.GetRequiredService<IDataProtectionProvider>(), SecureVaultProfile.Operations));
builder.Services.AddKeyedSingleton("personal", (provider, _) => new SecureFileService(
    provider.GetRequiredService<IWebHostEnvironment>(), provider.GetRequiredService<IDataProtectionProvider>(), SecureVaultProfile.Personal));
builder.Services.AddScoped(provider =>
{
    var path = provider.GetRequiredService<IHttpContextAccessor>().HttpContext?.Request.Path ?? default;
    return provider.GetRequiredKeyedService<SecureFileService>(path.StartsWithSegments("/api/personal") ? "personal" : "operations");
});
builder.Services.AddSingleton<UserStore>();
builder.Services.AddSingleton<ProgressStore>();
builder.Services.AddSingleton<LearningLibrary>();
builder.Services.AddSingleton<FeatureLicenseImportService>();
builder.Services.AddSingleton<KeyVaultLicenseService>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "srehub.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(Math.Clamp(authOptions.SessionHours, 1, 168));
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.Services.GetRequiredService<UserStore>();

app.UseAuthentication();
app.UseAuthorization();

// Everything except the sign-in page and its assets requires a session.
string[] anonymousPaths = ["/login.html", "/css/", "/js/", "/favicon.ico"];

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "/";
    var isApi = path.StartsWith("/api", StringComparison.OrdinalIgnoreCase);

    if (isApi)
    {
        context.Response.Headers.CacheControl = "no-store";
    }

    var allowAnonymous = path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase)
        || anonymousPaths.Any(allowed => path.StartsWith(allowed, StringComparison.OrdinalIgnoreCase));

    if (context.User.Identity?.IsAuthenticated != true && !allowAnonymous)
    {
        if (isApi)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Your session has expired. Sign in again." });
            return;
        }

        context.Response.Redirect("/login.html");
        return;
    }

    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    // Always revalidate so UI changes show without a hard refresh.
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache"
});

static async Task<IResult> RunAsync<T>(Func<Task<T>> action)
{
    try
    {
        return Results.Ok(await action());
    }
    catch (UnauthorizedAccessException exception)
    {
        return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status403Forbidden);
    }
    catch (FileNotFoundException exception)
    {
        return Results.NotFound(new { error = exception.Message });
    }
    catch (DirectoryNotFoundException exception)
    {
        return Results.NotFound(new { error = exception.Message });
    }
    catch (Exception exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
}

var auth = app.MapGroup("/api/auth");

auth.MapPost("/login", async (LoginRequest request, UserStore users, HttpContext context) =>
{
    var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    if (!users.TryAuthenticate(request.Email, request.Password, client, out var user, out var error))
    {
        return Results.Json(new { error }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user!.Email),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.DisplayName)
        ],
        CookieAuthenticationDefaults.AuthenticationScheme);

    await context.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity),
        new AuthenticationProperties { IsPersistent = true });

    return Results.Ok(new { email = user.Email, displayName = user.DisplayName });
});

auth.MapPost("/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok(new { ok = true });
});

auth.MapGet("/me", (HttpContext context) =>
{
    if (context.User.Identity?.IsAuthenticated != true)
    {
        return Results.Json(new { error = "Not signed in." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    return Results.Ok(new
    {
        email = context.User.FindFirstValue(ClaimTypes.Email),
        displayName = context.User.Identity.Name
    });
});

var learning = app.MapGroup("/api/learning").DisableAntiforgery();

learning.MapGet("/", (LearningLibrary library) => Results.Ok(new { contentRoot = library.ContentRoot }));

learning.MapGet("/subjects", (LearningLibrary library, CancellationToken cancellationToken) =>
    RunAsync(() => library.GetSubjectsAsync(cancellationToken)));

learning.MapGet("/subjects/{subjectId}", (string subjectId, LearningLibrary library, CancellationToken cancellationToken) =>
    RunAsync(() => library.GetSubjectAsync(subjectId, cancellationToken)));

learning.MapPost("/subjects", (CreateSubjectRequest request, LearningLibrary library, CancellationToken cancellationToken) =>
    RunAsync(() => library.CreateSubjectAsync(request, cancellationToken)));

learning.MapGet("/file", (string subject, string path, LearningLibrary library, CancellationToken cancellationToken) =>
    RunAsync(() => library.GetFileAsync(subject, path, cancellationToken)));

learning.MapGet("/resolve", (string subject, string? from, string href, LearningLibrary library) =>
    RunAsync(() => Task.FromResult(library.ResolveLink(subject, from ?? string.Empty, href))));

learning.MapGet("/raw", (string subject, string path, LearningLibrary library) =>
{
    try
    {
        var (fullPath, contentType) = library.ResolveRawFile(subject, path);
        return Results.File(fullPath, contentType, enableRangeProcessing: true);
    }
    catch (UnauthorizedAccessException exception)
    {
        return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status403Forbidden);
    }
    catch (Exception exception)
    {
        return Results.NotFound(new { error = exception.Message });
    }
});

learning.MapPost("/notes", (SaveNoteRequest request, LearningLibrary library, CancellationToken cancellationToken) =>
    RunAsync(() => library.SaveNoteAsync(request, cancellationToken)));

learning.MapPost("/upload", async (
    HttpRequest httpRequest,
    LearningLibrary library,
    LearningOptions options,
    CancellationToken cancellationToken) =>
{
    if (!httpRequest.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Expected a multipart form upload." });
    }

    var form = await httpRequest.ReadFormAsync(cancellationToken);
    var subjectId = form["subjectId"].ToString();
    var folder = form["folder"].ToString();
    var files = form.Files;

    if (files.Count == 0)
    {
        return Results.BadRequest(new { error = "No files were uploaded." });
    }

    return await RunAsync(async () =>
    {
        var saved = new List<LearningFileContent>();

        foreach (var file in files)
        {
            if (file.Length > options.MaxUploadBytes)
            {
                throw new InvalidOperationException($"'{file.FileName}' exceeds the {options.MaxUploadBytes / (1024 * 1024)} MB upload limit.");
            }

            await using var stream = file.OpenReadStream();
            saved.Add(await library.SaveUploadAsync(subjectId, folder, file.FileName, stream, cancellationToken));
        }

        return saved;
    });
});

learning.MapGet("/search", (string q, string? subject, LearningLibrary library, CancellationToken cancellationToken) =>
    RunAsync(() => library.SearchAsync(q, subject, cancellationToken)));

learning.MapGet("/flashcards", (string? subject, LearningLibrary library, CancellationToken cancellationToken) =>
    RunAsync(() => library.GetFlashcardsAsync(subject, cancellationToken)));

learning.MapPost("/progress", (ProgressRequest request, LearningLibrary library, CancellationToken cancellationToken) =>
    RunAsync(async () =>
    {
        await library.SetProgressAsync(request, cancellationToken);
        return new { ok = true };
    }));

learning.MapDelete("/file", (string subject, string path, LearningLibrary library, CancellationToken cancellationToken) =>
    RunAsync(async () =>
    {
        await library.DeleteFileAsync(subject, path, cancellationToken);
        return new { ok = true };
    }));

learning.MapDelete("/subjects/{subjectId}", (string subjectId, LearningLibrary library, CancellationToken cancellationToken) =>
    RunAsync(async () =>
    {
        await library.DeleteSubjectAsync(subjectId, cancellationToken);
        return new { ok = true };
    }));

learning.MapPost("/subjects/{subjectId}/rename", (string subjectId, RenameSubjectRequest request, LearningLibrary library, CancellationToken cancellationToken) =>
    RunAsync(() => library.RenameSubjectAsync(subjectId, request.NewName, cancellationToken)));

learning.MapPost("/folders", (FolderRequest request, LearningLibrary library, CancellationToken cancellationToken) =>
    RunAsync(() => library.CreateFolderAsync(request, cancellationToken)));

learning.MapPost("/rename", (RenameRequest request, LearningLibrary library, CancellationToken cancellationToken) =>
    RunAsync(() => library.RenameAsync(request, cancellationToken)));

learning.MapPost("/move", (MoveRequest request, LearningLibrary library, CancellationToken cancellationToken) =>
    RunAsync(() => library.MoveAsync(request, cancellationToken)));

var repositories = app.MapGroup("/api/repositories");

repositories.MapGet("/", (RepositoryService service, CancellationToken cancellationToken) =>
    RunAsync(async () => new { root = service.Root, repositories = await service.ListAsync(cancellationToken) }));

repositories.MapPost("/", (CloneRepositoryRequest request, RepositoryService service, CancellationToken cancellationToken) =>
    RunAsync(() => service.CloneAsync(request, cancellationToken)));

repositories.MapPost("/{name}/fetch", (string name, RepositoryService service, CancellationToken cancellationToken) =>
    RunAsync(() => service.FetchAsync(name, cancellationToken)));

repositories.MapPost("/{name}/pull", (string name, RepositoryService service, CancellationToken cancellationToken) =>
    RunAsync(() => service.PullAsync(name, cancellationToken)));

repositories.MapGet("/{name}/branches", (string name, RepositoryService service, CancellationToken cancellationToken) =>
    RunAsync(() => service.GetBranchesAsync(name, cancellationToken)));

repositories.MapPost("/{name}/switch", (string name, SwitchBranchRequest request, RepositoryService service, CancellationToken cancellationToken) =>
    RunAsync(() => service.SwitchBranchAsync(name, request.Branch, cancellationToken)));

repositories.MapPost("/{name}/open", (string name, OpenRepositoryRequest request, RepositoryService service) =>
    RunAsync(() =>
    {
        service.Open(name, request.Target);
        return Task.FromResult(new { ok = true });
    }));

repositories.MapDelete("/{name}", (string name, bool? force, RepositoryService service, CancellationToken cancellationToken) =>
    RunAsync(async () =>
    {
        await service.RemoveAsync(name, force ?? false, cancellationToken);
        return new { ok = true };
    }));

var importantFiles = app.MapGroup("/api/files").DisableAntiforgery();

importantFiles.MapGet("/", (ImportantFileService service) => Results.Ok(service.List()));

importantFiles.MapGet("/raw", (string name, ImportantFileService service) =>
{
    try
    {
        var (path, contentType) = service.Resolve(name);
        return Results.File(path, contentType, enableRangeProcessing: true);
    }
    catch (FileNotFoundException exception)
    {
        return Results.NotFound(new { error = exception.Message });
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

importantFiles.MapPost("/upload", async (HttpRequest httpRequest, ImportantFileService service, LearningOptions options, CancellationToken cancellationToken) =>
{
    if (!httpRequest.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Expected a multipart form upload." });
    }

    var form = await httpRequest.ReadFormAsync(cancellationToken);

    if (form.Files.Count == 0)
    {
        return Results.BadRequest(new { error = "No files were uploaded." });
    }

    return await RunAsync(async () =>
    {
        var saved = new List<ImportantFileInfo>();

        foreach (var file in form.Files)
        {
            if (file.Length > options.MaxUploadBytes)
            {
                throw new InvalidOperationException($"'{file.FileName}' exceeds the {options.MaxUploadBytes / (1024 * 1024)} MB upload limit.");
            }

            await using var stream = file.OpenReadStream();
            saved.Add(await service.SaveAsync(Path.GetFileName(file.FileName), stream, cancellationToken));
        }

        return saved;
    });
});

importantFiles.MapPost("/new", (string name, ImportantFileService service, CancellationToken cancellationToken) =>
    RunAsync(() => service.CreateBlankAsync(name, cancellationToken)));

importantFiles.MapDelete("/", (string name, ImportantFileService service) =>
    RunAsync(() =>
    {
        service.Delete(name);
        return Task.FromResult(new { ok = true });
    }));

var workLog = app.MapGroup("/api/worklog");

workLog.MapGet("/", (string month, WorkLogService service, CancellationToken cancellationToken) =>
    RunAsync(() => service.GetMonthAsync(month, cancellationToken)));

workLog.MapGet("/year", (int year, WorkLogService service, CancellationToken cancellationToken) =>
    RunAsync(() => service.GetYearAsync(year, cancellationToken)));

workLog.MapPut("/{date}", (string date, WorkLogEntry entry, WorkLogService service, CancellationToken cancellationToken) =>
    RunAsync(() => service.SaveAsync(date, entry, cancellationToken)));

workLog.MapGet("/export", async (string period, WorkLogService service, CancellationToken cancellationToken) =>
{
    try
    {
        var csv = await service.ExportCsvAsync(period, cancellationToken);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        return Results.File(bytes, "text/csv", $"worklog-{period}.csv");
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

var dutyLog = app.MapGroup("/api/dutylog");

dutyLog.MapGet("/year", (int year, DutyLogService service, CancellationToken cancellationToken) =>
    RunAsync(() => service.GetYearAsync(year, cancellationToken)));

dutyLog.MapPut("/{date}", (string date, DutyEntry entry, DutyLogService service, CancellationToken cancellationToken) =>
    RunAsync(() => service.SaveAsync(date, entry, cancellationToken)));

dutyLog.MapGet("/roster", (DutyLogService service, CancellationToken cancellationToken) =>
    RunAsync(() => service.GetRosterAsync(cancellationToken)));

dutyLog.MapPut("/roster", (DutyRoster roster, DutyLogService service, CancellationToken cancellationToken) =>
    RunAsync(() => service.SaveRosterAsync(roster, cancellationToken)));

dutyLog.MapGet("/export", async (string from, string to, DutyLogService service, CancellationToken cancellationToken) =>
{
    try
    {
        var csv = await service.ExportCsvAsync(from, to, cancellationToken);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        return Results.File(bytes, "text/csv", "dutylog.csv");
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

dutyLog.MapGet("/export-onenote", async (string from, string to, DutyLogService service, CancellationToken cancellationToken) =>
{
    try
    {
        var csv = await service.ExportOneNoteCsvAsync(from, to, cancellationToken);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        return Results.File(bytes, "text/csv", "onenote-updates.csv");
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

MapVault(app.MapGroup("/api/secure").DisableAntiforgery());
MapVault(app.MapGroup("/api/personal").DisableAntiforgery());

void MapVault(RouteGroupBuilder secure)
{

    static string CurrentUser(HttpContext context) => context.User.FindFirstValue(ClaimTypes.Email) ?? "unknown";

    secure.MapGet("/tree", (SecureFileService service, UserStore users, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            var tree = await service.GetTreeAsync(cancellationToken);
            foreach (var file in tree.Files)
            {
                file.UpdatedBy = users.GetDisplayName(file.UpdatedBy);
                foreach (var version in file.Versions)
                {
                    version.UploadedBy = users.GetDisplayName(version.UploadedBy);
                }
            }

            return tree;
        }));

    secure.MapPost("/folders", (SecureFolderRequest request, HttpContext context, SecureFileService service, CancellationToken cancellationToken) =>
        RunAsync(() => service.CreateFolderAsync(request.Name, request.ParentId, CurrentUser(context), cancellationToken)));

    secure.MapPut("/folders/{id}", (string id, SecureFolderRequest request, HttpContext context, SecureFileService service, CancellationToken cancellationToken) =>
        RunAsync(() => service.UpdateFolderAsync(id, request.Name, request.ParentId, CurrentUser(context), cancellationToken)));

    secure.MapDelete("/folders/{id}", (string id, HttpContext context, SecureFileService service, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            await service.DeleteFolderAsync(id, CurrentUser(context), cancellationToken);
            return new { ok = true };
        }));

    secure.MapPost("/files", async (HttpRequest request, HttpContext context, SecureFileService service, LearningOptions options, CancellationToken cancellationToken) =>
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new { error = "Expected a multipart form upload." });
        }

        var form = await request.ReadFormAsync(cancellationToken);

        if (form.Files.Count == 0)
        {
            return Results.BadRequest(new { error = "No files were uploaded." });
        }

        var meta = new SecureFileMetaRequest
        {
            Type = form["type"].ToString(),
            Environment = form["environment"].ToString(),
            Infrastructure = form["infrastructure"].ToString(),
            Expiry = form["expiry"].ToString(),
            Notes = form["notes"].ToString()
        };

        return await RunAsync(async () =>
        {
            var saved = new List<SecureFile>();

            foreach (var file in form.Files)
            {
                if (file.Length > options.MaxUploadBytes)
                {
                    throw new InvalidOperationException($"'{file.FileName}' exceeds the {options.MaxUploadBytes / (1024 * 1024)} MB upload limit.");
                }

                await using var stream = file.OpenReadStream();
                saved.Add(await service.AddFileAsync(form["folderId"].ToString(), file.FileName, stream, meta, CurrentUser(context), cancellationToken));
            }

            return saved;
        });
    });

    secure.MapPut("/files/{id}", (string id, SecureFileMetaRequest request, HttpContext context, SecureFileService service, CancellationToken cancellationToken) =>
        RunAsync(() => service.UpdateFileAsync(id, request, CurrentUser(context), cancellationToken)));

    secure.MapPost("/files/{id}/version", async (string id, HttpRequest request, HttpContext context, SecureFileService service, LearningOptions options, CancellationToken cancellationToken) =>
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new { error = "Expected a multipart form upload." });
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.FirstOrDefault();

        if (file is null)
        {
            return Results.BadRequest(new { error = "No file was uploaded." });
        }

        if (file.Length > options.MaxUploadBytes)
        {
            return Results.BadRequest(new { error = $"'{file.FileName}' exceeds the {options.MaxUploadBytes / (1024 * 1024)} MB upload limit." });
        }

        return await RunAsync(async () =>
        {
            await using var stream = file.OpenReadStream();
            return await service.AddVersionAsync(id, stream, CurrentUser(context), cancellationToken);
        });
    });

    secure.MapGet("/files/{id}/download", async (string id, int? version, HttpContext context, SecureFileService service, CancellationToken cancellationToken) =>
    {
        try
        {
            var (data, name) = await service.OpenAsync(id, version, CurrentUser(context), cancellationToken);
            return Results.File(data, "application/octet-stream", name);
        }
        catch (FileNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    });

    secure.MapGet("/files/{id}/text", (string id, HttpContext context, SecureFileService service, CancellationToken cancellationToken) =>
        RunAsync(() => service.ReadTextAsync(id, CurrentUser(context), cancellationToken)));

    secure.MapDelete("/files/{id}", (string id, HttpContext context, SecureFileService service, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            await service.DeleteFileAsync(id, CurrentUser(context), cancellationToken);
            return new { ok = true };
        }));

    secure.MapGet("/trash", (SecureFileService service, CancellationToken cancellationToken) =>
        RunAsync(() => service.GetTrashAsync(cancellationToken)));

    secure.MapPost("/trash/{kind}/{id}/restore", (string kind, string id, HttpContext context, SecureFileService service, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            await service.RestoreAsync(kind, id, CurrentUser(context), cancellationToken);
            return new { ok = true };
        }));

    secure.MapDelete("/trash/{kind}/{id}", (string kind, string id, HttpContext context, SecureFileService service, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            await service.PurgeAsync(kind, id, CurrentUser(context), cancellationToken);
            return new { ok = true };
        }));

    secure.MapGet("/audit", (int? limit, SecureFileService service, UserStore users, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            var entries = await service.GetAuditAsync(limit ?? 200, cancellationToken);
            foreach (var entry in entries)
            {
                entry.User = users.GetDisplayName(entry.User);
            }

            return entries;
        }));
}

var maths = app.MapGroup("/api/maths");

maths.MapGet("/", (MathsLibraryService service) =>
    RunAsync(() => Task.FromResult(service.GetLibrary())));

maths.MapGet("/book", (MathsLibraryService service) =>
    RunAsync(() => Task.FromResult(service.GetBook())));

maths.MapGet("/file", (string topic, string name, MathsLibraryService service, HttpContext context) =>
{
    try
    {
        var path = service.ResolveFile(topic, name);
        context.Response.Headers.CacheControl = "private, max-age=3600";
        return Results.File(path, "application/pdf", enableRangeProcessing: true);
    }
    catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
    {
        return Results.NotFound(new { error = exception.Message });
    }
});

maths.MapGet("/notes", (string topic, MathsLibraryService service) =>
    RunAsync(() => Task.FromResult(service.GetNote(topic))));

maths.MapPut("/notes", (string topic, SaveMathsNoteRequest request, MathsLibraryService service) =>
    RunAsync(() => Task.FromResult(service.SaveNote(topic, request.Content))));

maths.MapPut("/status", (string topic, SaveMathsStatusRequest request, MathsLibraryService service) =>
    RunAsync(() => Task.FromResult(new { status = service.SaveStatus(topic, request.Status) })));

var links = app.MapGroup("/api/links");

links.MapGet("/", (ImportantLinkService service, CancellationToken cancellationToken) =>
    RunAsync(() => service.GetAllAsync(cancellationToken)));

links.MapPost("/", (ImportantLinkRequest link, ImportantLinkService service, CancellationToken cancellationToken) =>
    RunAsync(() => service.SaveAsync(null, link, cancellationToken)));

links.MapPut("/{id}", (string id, ImportantLinkRequest link, ImportantLinkService service, CancellationToken cancellationToken) =>
    RunAsync(() => service.SaveAsync(id, link, cancellationToken)));

links.MapGet("/{id}/password", (string id, ImportantLinkService service, CancellationToken cancellationToken) =>
    RunAsync(async () => new LinkPasswordResponse(await service.RevealPasswordAsync(id, cancellationToken))));

links.MapDelete("/{id}", (string id, ImportantLinkService service, CancellationToken cancellationToken) =>
    RunAsync(async () =>
    {
        await service.DeleteAsync(id, cancellationToken);
        return new { ok = true };
    }));

var tracker = app.MapGroup("/api/tracker");

tracker.MapGet("/", (TrackedWorkService service, CancellationToken cancellationToken) =>
    RunAsync(() => service.GetAllAsync(cancellationToken)));

tracker.MapPost("/", (TrackedWorkItem item, TrackedWorkService service, CancellationToken cancellationToken) =>
    RunAsync(() => service.SaveAsync(null, item, cancellationToken)));

tracker.MapPost("/import", (TrackedWorkImportRequest request, TrackedWorkService service, CancellationToken cancellationToken) =>
    RunAsync(() => service.ImportAsync(request.Items, cancellationToken)));

tracker.MapPut("/{id}", (string id, TrackedWorkItem item, TrackedWorkService service, CancellationToken cancellationToken) =>
    RunAsync(() => service.SaveAsync(id, item, cancellationToken)));

tracker.MapDelete("/{id}", (string id, TrackedWorkService service, CancellationToken cancellationToken) =>
    RunAsync(async () =>
    {
        await service.DeleteAsync(id, cancellationToken);
        return new { ok = true };
    }));

var assistant = app.MapGroup("/api/assistant");

assistant.MapGet("/status", (AssistantService service) => Results.Ok(service.Status));

assistant.MapPost("/chat", (AssistantChatRequest request, HttpContext context, AssistantService service, CancellationToken cancellationToken) =>
    RunAsync(() => service.ChatAsync(request.ConversationId, request.Message, context.User.FindFirstValue(ClaimTypes.Email) ?? "unknown", context.User.FindFirstValue(ClaimTypes.Name) ?? string.Empty, cancellationToken)));

assistant.MapPost("/approve", (AssistantResolveRequest request, HttpContext context, AssistantService service, CancellationToken cancellationToken) =>
    RunAsync(() => service.ResolveAsync(request.ConversationId, request.Approve, context.User.FindFirstValue(ClaimTypes.Email) ?? "unknown", cancellationToken)));

assistant.MapPost("/reset", (AssistantResetRequest request, HttpContext context, AssistantService service) =>
{
    service.Reset(request.ConversationId, context.User.FindFirstValue(ClaimTypes.Email) ?? "unknown");
    return Results.Ok(new { ok = true });
});

var terminal = app.MapGroup("/api/terminal");

terminal.MapPost("/sessions", (HttpContext context, TerminalService service) =>
{
    var email = context.User.FindFirstValue(ClaimTypes.Email);

    if (!service.IsAllowed(email))
    {
        return Results.Json(new { error = "Your account is not allowed to open a shell." }, statusCode: StatusCodes.Status403Forbidden);
    }

    try
    {
        return Results.Ok(service.Create(email!));
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

terminal.MapPost("/sessions/{id}/run", (string id, TerminalRunRequest request, HttpContext context, TerminalService service) =>
{
    var email = context.User.FindFirstValue(ClaimTypes.Email);
    var session = service.IsAllowed(email) ? service.Find(id, email!) : null;

    if (session is null)
    {
        return Results.NotFound(new { error = "This shell session no longer exists." });
    }

    if (string.IsNullOrWhiteSpace(request.Command))
    {
        return Results.BadRequest(new { error = "Enter a command." });
    }

    if (session.IsBusy)
    {
        return Results.Json(new { error = "A command is already running in this session. Stop it first." }, statusCode: StatusCodes.Status409Conflict);
    }

    var lineOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    var aborted = context.RequestAborted;

    return Results.Stream(async stream =>
    {
        try
        {
            await foreach (var item in service.RunAsync(session, request.Command, aborted))
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(item, lineOptions) + "\n"), CancellationToken.None);
                await stream.FlushAsync(CancellationToken.None);
            }
        }
        catch (IOException)
        {
            // The browser closed the connection; the session was already stopped.
        }
    }, "application/x-ndjson");
});

terminal.MapPost("/sessions/{id}/stop", (string id, HttpContext context, TerminalService service) =>
{
    var email = context.User.FindFirstValue(ClaimTypes.Email);
    service.Find(id, email ?? string.Empty)?.Stop();
    return Results.Ok(new { ok = true });
});

terminal.MapDelete("/sessions/{id}", (string id, HttpContext context, TerminalService service) =>
{
    service.Close(id, context.User.FindFirstValue(ClaimTypes.Email) ?? string.Empty);
    return Results.Ok(new { ok = true });
});

app.MapPost("/api/import", (ImportRequest request, FeatureLicenseImportService service, CancellationToken cancellationToken) =>
    RunAsync(() => service.RunAsync(request, cancellationToken)));

app.MapPost("/api/keyvault", (KeyVaultRequest request, KeyVaultLicenseService service, CancellationToken cancellationToken) =>
    RunAsync(() => service.UpdateAsync(request, cancellationToken)));

app.MapPost("/api/licenses/preview", (PreviewRequest request) =>
    RunAsync(() =>
    {
        var file = LicenseFileParser.Parse(request.LicenseFileContent);

        return Task.FromResult<object>(new
        {
            licenses = file.LicenseData.Select(license => new
            {
                keyName = license.KeyName,
                expirationDate = license.ExpirationDate,
                hasAzure = !string.IsNullOrWhiteSpace(license.ValueAzure),
                hasBuildCi = !string.IsNullOrWhiteSpace(license.ValueBuildCi)
            }),
            frameworkLicenses = file.FrameworkLicenses.Select(framework => framework.KeyName)
        });
    }));

app.Run();

return 0;

static int SetPassword(string contentRoot, string? email)
{
    var storePath = Path.Combine(contentRoot, "App_Data", "users.json");
    var serializerOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    List<AuthUser> users;

    try
    {
        users = File.Exists(storePath)
            ? JsonSerializer.Deserialize<List<AuthUser>>(File.ReadAllText(storePath), serializerOptions) ?? []
            : [];
    }
    catch (JsonException exception)
    {
        Console.Error.WriteLine($"Could not read {storePath}: {exception.Message}");
        return 1;
    }

    if (string.IsNullOrWhiteSpace(email))
    {
        Console.Write("Email: ");
        email = Console.ReadLine();
    }

    email = (email ?? string.Empty).Trim();

    if (email.Length == 0)
    {
        Console.Error.WriteLine("No email entered.");
        return 1;
    }

    Console.Write("New password: ");
    var password = ReadHidden();
    Console.WriteLine();

    Console.Write("Confirm password: ");
    var confirmation = ReadHidden();
    Console.WriteLine();

    if (string.IsNullOrWhiteSpace(password))
    {
        Console.Error.WriteLine("No password entered.");
        return 1;
    }

    if (!string.Equals(password, confirmation, StringComparison.Ordinal))
    {
        Console.Error.WriteLine("Passwords do not match.");
        return 1;
    }

    var user = users.FirstOrDefault(candidate => string.Equals(candidate.Email, email, StringComparison.OrdinalIgnoreCase));

    if (user is null)
    {
        user = new AuthUser { Email = email, DisplayName = email };
        users.Add(user);
    }

    user.PasswordHash = PasswordHasher.Hash(password);

    Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
    File.WriteAllText(storePath, JsonSerializer.Serialize(users, serializerOptions));

    Console.WriteLine($"Password updated for {user.Email} in {storePath}.");
    return 0;
}

static string ReadHidden()
{
    var value = new System.Text.StringBuilder();

    while (true)
    {
        var key = Console.ReadKey(intercept: true);

        if (key.Key == ConsoleKey.Enter)
        {
            return value.ToString();
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (value.Length > 0)
            {
                value.Length--;
            }

            continue;
        }

        if (!char.IsControl(key.KeyChar))
        {
            value.Append(key.KeyChar);
        }
    }
}
