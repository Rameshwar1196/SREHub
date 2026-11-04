# 14 — Work Journal

What I actually did at work, with the evidence attached. Written for two audiences: me in six months
when I've forgotten the detail, and an interviewer who asks "tell me about a time when...".

Keep entries short and factual. Record the **numbers** while you still have them — you will not be able
to reconstruct "p99 went from 1.8 s to 340 ms" a year later.

## How to add an entry

1. Copy [entry-template.md](entry-template.md) into `entries/` as `YYYY-MM-short-slug.md`.
2. Fill it in. Two paragraphs is plenty; the value is in the numbers and the evidence.
3. Upload supporting files through **Add content → Upload files** with the target folder
   `files/<same-slug>` — dashboards screenshots, PR links exported as PDF, postmortems, pipeline YAML,
   architecture diagrams (`.drawio`), query exports.
4. Add a row to the index below.
5. Mark the entry **Completed** once it has a STAR-ready story attached.

Folder convention:

```text
14-work-journal/
  README.md                         this index
  entry-template.md                 copy this for an achievement
  kb-article-template.md            copy this when writing a KB article
  entries/2026-02-alert-noise.md    one file per achievement
  files/2026-02-alert-noise/        evidence for that entry
  kb/                               Aras knowledgebase reference copy (confidential)
```

## Knowledgebase reference copy

[kb/](kb/README.md) holds 300 Aras knowledgebase articles imported from the DAMON export, grouped by
section. Each article exists twice:

- `<category>/<slug>.md` — converted Markdown. Previewable and **searchable** in SRE Hub.
- `<category>/source/<slug>.docx` — the untouched original. Open this when you need screenshots or
  exact formatting; the Markdown is text-only.

To re-import after a fresh export:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
cd d:\ManagedServices\Work\SreHub
.\tools\Import-KbArticles.ps1 -SourcePath '<export folder>' `
                              -DestinationPath '.\learning-content\14-work-journal\kb' -Force
```

The importer slugifies names (SRE Hub rejects path segments that do not start with a letter or digit,
and every exported file starts with `(7) `), strips the browser print headers and footers, lifts tags
and the source URL into metadata, rebuilds tables, and ignores repeat exports of the same article.

Writing a new article? Copy [kb-article-template.md](kb-article-template.md) — it follows the same
Description / Prerequisites / Steps / Validation / Rollback / Troubleshooting / References shape as
the published articles.

## Index

| # | Date | Entry | Theme | Impact |
|---|------|-------|-------|--------|
| 1 | 2026-02 | [Cut on-call alert noise by 70%](entries/2026-02-alert-noise.md) | Observability, toil | 41 → 12 pages/week |
| 2 | | | | |
| 3 | | | | |

## What makes a good entry

- **Scope and ownership.** Did you lead it, contribute, or review? Be honest — interviewers probe this.
- **Numbers before and after.** Latency, error rate, pages per shift, cost per month, deploy frequency,
  MTTR, hours of toil removed per week. "Improved reliability" is worthless; "error budget burn dropped
  from 3.2x to 0.6x" is not.
- **The decision you made, and the one you rejected.** Trade-offs are the signal.
- **What went wrong.** The entries where something broke are the ones that answer the hardest interview
  questions. Write those down too.
- **Evidence.** A link rots when you change employer. Export it: PDF the PR, screenshot the dashboard,
  save the postmortem.

## Themes to tag entries with

Reliability · Incident response · Observability · Automation / toil reduction · Cost (FinOps) ·
Security · Migration · Performance · Platform / developer experience · Mentoring · Process

Tagging makes it easy to find a story for a specific interview round — see
[../11-interview-prep/star-stories-template.md](../11-interview-prep/star-stories-template.md).

## Review cadence

- **Weekly, 10 minutes.** Add anything worth remembering from the week. Do not wait for "big" wins.
- **Quarterly.** Promote the three strongest entries into full STAR stories in `11-interview-prep`.
- **Before any interview or review cycle.** Re-read the index and pick stories per competency.

## Privacy

Two different things live in this module, and they have different rules.

**`entries/` and `files/` are yours.** Personal, portable, safe to keep when you change employer.
Keep them free of anything you are not allowed to take with you:

- No customer names, credentials, tokens, internal hostnames, or IP ranges.
- No copies of proprietary source or internal-only documents.
- Anonymise: "a European manufacturing customer", "~2,000 users", "the payments service".
- Record *your* contribution and public-safe metrics, not the employer's confidential data.

**`kb/` is Aras property.** It is an internal knowledgebase copy held here for day-to-day reference
only. Do not commit it to a personal repository, do not sync it to personal cloud storage, and delete
it when you no longer work on Aras systems. When exporting this journal for your own use, take
`entries/` and `files/` and leave `kb/` behind.
