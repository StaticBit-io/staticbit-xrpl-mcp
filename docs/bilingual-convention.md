# Bilingual documentation convention

The project ships documentation in **two languages** — English (default) and Russian. We use the *parallel-file* convention popularized by the upstream [XrplCSharp](https://github.com/StaticBit-io/XrplCSharp/tree/release/DocFx) repo.

## File naming

```
docs/features.md           # English — primary, what GitHub shows by default
docs/features.ru.md        # Russian sibling
docs/examples/foo.md       # English
docs/examples/foo.ru.md    # Russian
```

Always two files per logical document. **English is the source-of-truth** for cross-references from code, integration tests, and CI commit messages — it's what an international audience reaches first.

## Language-switcher banner

Every page opens with a one-line banner that points to its sibling:

**On English pages:**
```markdown
> 🇷🇺 [Прочесть на русском](filename.ru.md)
```

**On Russian pages:**
```markdown
> 🇬🇧 [Read in English](filename.md)
```

Place it as the **first line** of the file, before the H1 heading. Renders as a soft quote block at the top of the rendered page on GitHub.

## What to translate / what to keep

- **Document body**: full translation. Both sides should be functionally equivalent — same sections, same tables, same code snippets.
- **Code examples**: keep identical between languages — they reference the same XRPL/MCP tool names.
- **Tool names** (e.g. `xrpl_payment_prepare`) and **MCP-level error codes** (`tecNO_PERMISSION`): never translate.
- **Markdown anchors and cross-page links**: keep relative paths consistent — `[features](features.md)` on EN side, `[features](features.ru.md)` on RU side. Linkcheck via grep before commit.
- **Code comments**: keep in English (per global C# style rules) — both EN and RU docs reference the same code.

## When the two versions drift

If you update one side and not the other, mark the stale side with a banner directly under the language switcher:

```markdown
> 🇬🇧 [Read in English](features.md)
>
> ⚠️ Эта страница может отставать от английской версии — последние изменения см. в EN.
```

The expectation is parity. Drift is a TODO to close in the next sync.

## Adding a new document

1. Write the English `.md` first.
2. Add the language-switcher banner at the top.
3. Translate to `.ru.md` (or mark as "translation pending" until done).
4. Add the **English** filename to the parent TOC / index.
5. Cross-reference: the RU sibling is reachable via the banner — no separate TOC needed.

## Renaming an existing Russian doc

When porting historical RU docs into the bilingual convention:

```bash
git mv docs/features.md docs/features.ru.md
# Now write docs/features.md as the English version.
# Add the banner to both files.
```

## Tools that follow the same convention

- [XrplCSharp/DocFx](https://github.com/StaticBit-io/XrplCSharp/tree/release/DocFx) — primary reference.
- Most open-source .NET projects with non-English maintainers (CefSharp, NLog, …).

## Out-of-scope

- **In-code XML doc-comments**: stay English-only (matches global C# style rules).
- **Commit messages**: English.
- **PR descriptions**: prefer English (auditable by international reviewers); Russian acceptable for tactical PRs.
- **Issue templates / labels**: English-only.
