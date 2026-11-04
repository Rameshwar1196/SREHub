# SRE Hub

A small local web app with two areas:

- **Learning** — browse, search, extend and track a markdown-based study library (subjects such as
  Kubernetes, IaC/Terraform, Azure DevOps, Observability), plus a flashcard mode built from the interview
  questions in those files.
- **Aras tooling** — import Aras Innovator feature licenses into one or more instances, and push license
  data into an Azure Key Vault.

ASP.NET Core 8 minimal API + a TypeScript front end. No database; the learning library is plain files on disk.

## Prerequisites

- .NET 8 SDK
- Node.js 18+ (only to build the TypeScript UI)
- For the Key Vault tab: `az login`, or a managed identity with **Key Vault Secrets Officer** on the vault

## Build and run

```powershell
cd d:\ManagedServices\Work\SreHub
npm.cmd install
npm.cmd run build        # compiles ClientApp/*.ts -> wwwroot/js
dotnet run -c Release
```

Open <http://localhost:5280>. Deep links: `#learning`, `#import`, `#vault`.

`npm.cmd` is used instead of `npm` because the PowerShell execution policy on this machine blocks `npm.ps1`.

## Sign in

The app requires a session. On first start, if no account exists, one is created and its password is
printed **once** to the console that started the app:

```text
   Email    : admin@local
   Password : <generated>
```

The hash is stored in `App_Data/users.json` (gitignored); the plaintext is never written anywhere.

To set your own password, or to add more accounts, generate a hash and put it in configuration:

```powershell
dotnet run -- hash-password          # prompts, prints a pbkdf2.sha256$... string
```

```json
"Auth": {
  "SeedEmail": "you@example.com",
  "SessionHours": 12,
  "MaxFailedAttempts": 5,
  "LockoutMinutes": 15,
  "Users": [
    { "Email": "you@example.com", "DisplayName": "Your Name", "PasswordHash": "pbkdf2.sha256$210000$...$..." }
  ]
}
```

Accounts in configuration take precedence over `App_Data/users.json`; delete that file to force a reseed.

How it is protected:

- Passwords are stored as **PBKDF2-SHA256, 210,000 iterations**, 16-byte salt, 32-byte key, compared in
  constant time. Plaintext is never stored or logged.
- Unknown accounts still run the key derivation, so a wrong email and a wrong password cost the same.
- **5 failed attempts** per email+IP triggers a **15 minute** lockout.
- Session is an `HttpOnly`, `SameSite=Strict` cookie with a 12 hour sliding expiry.
- Every path except `/login.html` and its static assets requires a session; API calls return `401` and
  the UI redirects to the sign-in page.

## Learning

The library is a folder of subjects. Each top-level folder under the content root is a subject; its
`README.md` supplies the title (first `#` heading) and description (first paragraph).

Content root is configured in [appsettings.json](appsettings.json) and defaults to the bundled study
library at [learning-content](learning-content):

```json
"Learning": {
  "ContentRoot": "learning-content",
  "MaxUploadBytes": 26214400,
  "MaxSearchHits": 200
}
```

| Capability | Notes |
|---|---|
| Browse | Subject list with progress bars, nested file tree, markdown rendered server-side, plain text view, inline image and PDF preview. |
| Add subject | Creates `<content root>/<name>/README.md` pre-seeded with Concepts / Commands / Interview questions headings. |
| Add / edit note | Writes a markdown or text file anywhere under the subject, including subfolders. "Edit" on a file loads it back into the editor. |
| Upload | Multi-file upload into an optional subfolder. Allowed: markdown, text/config/script files, images, `.pdf`, `.drawio`, `.zip`, `.docx`, `.pptx`, `.xlsx`. |
| Search | Case-insensitive full-text scan over all markdown/text files, scoped to one subject or the whole library. Click a hit to jump to the file. |
| Flashcards | Interview questions parsed out of the markdown, with previous / next / shuffle and reveal-answer. |
| Progress | A per-file "Completed" toggle, stored in `<content root>/.sre-hub/progress.json`. |

### Flashcard format

Two markdown shapes are recognised:

```markdown
## Interview questions with model answers

**Q: How would you define an SLO for a new service?**
Start with user journeys, pick 2-4 SLIs, baseline for four weeks, then agree an error-budget policy.
```

```markdown
## Interview questions

1. How do you manage Terraform state for 50 microservices across 4 environments?
2. Someone changed a resource in the portal. How do you detect and fix it?
```

The first form produces a card with an answer; the second produces question-only cards.

## Feature Licenses

Upload a license JSON file, pick the variant (`auto` / `key_value_azure` / `key_value_build_ci`), enter one
or more Innovator URLs and admin credentials. Per instance the app resolves the database from
`Server/dblist.aspx`, logs in with the OAuth password grant, imports each license through
`Server/Licensing.asmx/ImportFeatureLicense` unless it already exists with a matching expiration date,
re-reads it to confirm, then merges a temporary C# method that calls `ConsumeLicense` per feature and
deletes it again. This mirrors [Import-FeatureLicense.ps1](../Licenses/Import-FeatureLicense.ps1).

## Key Vault

Upload a license JSON file, enter the Build or Core vault name (short name or full URI) and the variant.
Each `key_name` becomes a secret holding the selected variant value; characters outside `[0-9a-zA-Z-]` are
replaced with `-`. Identical values report `Unchanged` so no new version is created. "Preview only" is on
by default and reports `WouldUpdate` without writing.

## API

| Endpoint | Purpose |
|---|---|
| `POST /api/auth/login` | sign in, issues the session cookie |
| `POST /api/auth/logout` | sign out |
| `GET /api/auth/me` | current user |
| `GET /api/learning` | content root |
| `GET /api/learning/subjects` | subject list with progress |
| `GET /api/learning/subjects/{id}` | subject detail, README html, file tree |
| `POST /api/learning/subjects` | create a subject |
| `GET /api/learning/file?subject=&path=` | file content, markdown rendered to html |
| `GET /api/learning/raw?subject=&path=` | raw bytes for images and PDFs |
| `POST /api/learning/notes` | create or overwrite a markdown/text file |
| `POST /api/learning/upload` | multipart upload |
| `GET /api/learning/search?q=&subject=` | full-text search |
| `GET /api/learning/flashcards?subject=` | parsed interview questions |
| `POST /api/learning/progress` | mark a file complete |
| `POST /api/licenses/preview` | parse a license JSON file |
| `POST /api/import` | run the Innovator import |
| `POST /api/keyvault` | write license data to Key Vault |

## Layout

```text
SreHub/
  Program.cs                    minimal API host and endpoint map
  Auth/
    PasswordHasher.cs           PBKDF2 hash/verify, password generation
    UserStore.cs                account loading, seeding, lockout
    AuthContracts.cs
  Learning/
    LearningOptions.cs          content root and limits
    LearningLibrary.cs          subjects, tree, read/write, upload, search
    FlashcardParser.cs          interview-question extraction
    ProgressStore.cs            per-file completion state
    LearningContracts.cs
  Models/                       license request and result contracts
  Services/
    LicenseFileParser.cs        license JSON parsing, variant resolution
    InnovatorClient.cs          dblist, OAuth, license import, AML
    FeatureLicenseImportService.cs
    KeyVaultLicenseService.cs
  ClientApp/                    TypeScript sources
  wwwroot/                      index.html, css, compiled js
  learning-content/             the study library (subjects, cheatsheets, labs, study plan)
```

## Security notes

- Run on `localhost`. There is no TLS, so the session cookie is `SameAsRequest`-secure only — do not
  expose this on a network without putting it behind HTTPS.
- Innovator credentials live only for the duration of one request and are never written to disk.
- All learning file access is confined to the content root: `..` segments are rejected and every resolved
  path is verified to stay inside the subject folder.
- Markdown is rendered with Markdig with raw HTML disabled, so content files cannot inject script.
- *Ignore TLS certificate errors* disables certificate validation for that run; use it only against
  internal test instances.
