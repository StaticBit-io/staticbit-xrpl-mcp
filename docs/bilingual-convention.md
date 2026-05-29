>  🌐 **Language**: **English** | [Русский](ru/bilingual-convention.md)

# Bilingual documentation convention

The project ships documentation in **two languages** — English (the canonical/default source-of-truth) and Russian (a faithful mirror). The layout is unified across **all** StaticBit MCP repositories and is enforced in CI by `scripts/check-translations.sh` (workflow `.github/workflows/docs-i18n.yml`).

## File layout

There are two placement rules depending on whether the file lives under `docs/`.

### Inside `docs/` — mirror subtree

English is canonical at its natural path; the Russian translation mirrors the same relative path under `docs/ru/`:

```
docs/features.md                 # English (canonical)
docs/ru/features.md              # Russian mirror

docs/examples/amm-clawback.md    # English (canonical)
docs/ru/examples/amm-clawback.md # Russian mirror
```

So `docs/<path>.md` ⇄ `docs/ru/<path>.md`. The `docs/ru/` subtree contains **only** translations — every file in it must have an English source at the corresponding `docs/<path>.md`, and every English page under `docs/` (outside `docs/ru/`) must have its mirror.

### Outside `docs/` — suffix sibling

For files at the repo root and under `plugins/*`, `examples/*`, `servers/*`, and `infra/`, the Russian translation is a `.ru.md` sibling next to the English `.md`:

```
README.md                        # English (canonical)
README.ru.md                     # Russian sibling

plugins/xrpl-cloud/README.md     # English (canonical)
plugins/xrpl-cloud/README.ru.md  # Russian sibling
```

So `<dir>/X.md` ⇄ `<dir>/X.ru.md`.

**English is the source-of-truth** for cross-references from code, integration tests, and CI commit messages — it's what an international audience reaches first.

## Language-switcher banner

Every page opens with a one-line banner that points to its counterpart. Place it as the **first line** of the file, before the H1 heading.

**On English pages:**
```markdown
>  🌐 **Language**: **English** | [Русский](<relative-path-to-russian>)
```

**On Russian pages:**
```markdown
>  🌐 **Язык**: [English](<relative-path-to-english>) | **Русский**
```

The relative target depends on depth. For a page under `docs/`, the Russian mirror sits one level deeper, e.g. from `docs/features.md` it is `ru/features.md`; from `docs/ru/features.md` the English source is `../features.md`. For `docs/ru/examples/x.md` the English source is `../../examples/x.md`. For suffix pairs the counterpart is a sibling in the same directory (`README.md` ⇄ `README.ru.md`).

## What to translate / what to keep

- **Document body**: full translation. Both sides must be functionally equivalent — same sections, same tables, same code snippets.
- **Code blocks / commands / paths / URLs / config keys**: keep **byte-identical** between languages — they reference the same XRPL/MCP entities.
- **Tool names** (e.g. `xrpl_payment_prepare`) and **engine result codes** (`tecNO_PERMISSION`): never translate.
- **Markdown cross-page links**: recompute relative paths for the file's location. From `docs/ru/<x>.md` a sibling translation is `<y>.md` (same `docs/ru/` subtree), the English source is `../<x>.md`, and a repo-root file is `../../<file>`.
- **Code comments**: keep in English (per global C# style rules).

## The CI gate

`scripts/check-translations.sh` (run locally with `bash scripts/check-translations.sh`) verifies that:

1. Every `docs/**/*.md` (excluding `docs/ru/`) has a `docs/ru/<rel>.md` mirror, and every `docs/ru/**/*.md` has an English source — no orphans.
2. Every suffix-zone `X.md` has an `X.ru.md` sibling.
3. Each Russian counterpart is **non-stub** (≥ 200 bytes).

### `.i18nignore`

English-only, generated, or agent-instruction files (e.g. `CHANGELOG.md`, generated catalogues, `CLAUDE.md` agent files) are exempted by listing their repo-relative path in the repo-root `.i18nignore` (one path per line; blank lines and `#` comments allowed). Do **not** translate agent-instruction `CLAUDE.md` files — exempt them instead.

### `.i18npairs` (optional)

For documents that live outside the conventional locations, an explicit `en-path:ru-path` mapping can be added to a repo-root `.i18npairs` file (one pair per line; `#` comments allowed).

## Adding a new document

1. Write the English file first (`docs/<path>.md` or `<dir>/X.md`).
2. Add the language-switcher banner at the top.
3. Create the Russian counterpart: `docs/ru/<path>.md` for docs, or `<dir>/X.ru.md` for the suffix zone.
4. Reference the **English** filename from the parent TOC / index.
5. Run `bash scripts/check-translations.sh` until it prints `OK: bilingual docs check passed.`

## Out-of-scope

- **In-code XML doc-comments**: stay English-only (matches global C# style rules).
- **Commit messages / PR descriptions / issue templates**: English.
- **Generated files and agent `CLAUDE.md` instructions**: not translated — exempt via `.i18nignore`.
