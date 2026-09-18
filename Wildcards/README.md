# Wildcard Packs

This folder is a **drop-in export** of SwarmUI wildcard files. It mirrors the exact folder
structure SwarmUI expects under `Data/Wildcards/`, so installing a pack is just a copy.

## What's a wildcard, in Swarm's format?

- A wildcard is a plain `.txt` file with **one option per line**.
- The file's path relative to `Data/Wildcards/` (minus the `.txt` extension) is its **name**.
  A file at `Data/Wildcards/general/color.txt` is named `general/color`.
- Lines starting with, or containing, `#` are treated as comments (everything from `#` onward
  on a line is stripped). Blank lines are ignored.
- You call a wildcard in a prompt with `<wildcard:name>` or the shorthand `<wc:name>`, e.g.
  `<wildcard:general/color>` or `<wc:general/color>`.
- Extras supported by Swarm's parser (no changes needed to these files to use them):
  - Exclude options: `<wildcard:general/color,not=red,blue>`
  - Pull more than one: `<wildcard[2-3]:general/color>` (same syntax as `<random[...]>`)
  - An optional same-name `.jpg` next to a `.txt` is used as a preview thumbnail in the UI.
    None of these packs ship a thumbnail; that's a cosmetic extra you can add per-file later.

## Installing a pack on a server

Copy the pack's folder into that server's `Data/Wildcards/` folder (same `Data` directory
configured in Server Settings → Paths, wherever it lives on disk), then either:

- Click the refresh icon on the gen page (it re-scans models *and* wildcards), or
- Open the "Wildcards" tab at the bottom of the gen page (this also triggers a re-scan), or
- Restart the server.

No rebuild or extension install is required — wildcards are just text files SwarmUI scans
at runtime.

## Packs in this folder

### `general/` — 24 files, 535 total options

A broad, tasteful, safe-for-everyone starter set: the kind of generic attribute you'd want to
randomize in almost *any* prompt, not tied to a specific fandom, genre, or NSFW use case.

| File | Options | What it's for |
|---|---:|---|
| `color.txt` | 45 | General color names |
| `color_palette.txt` | 20 | Palette/scheme descriptors (pastel, jewel tone, monochrome...) |
| `material.txt` | 24 | Surface/fabric materials (velvet, marble, brushed steel...) |
| `pattern.txt` | 20 | Patterns/prints (plaid, houndstooth, paisley...) |
| `hair_color.txt` | 24 | Hair colors, natural and fantasy |
| `hair_style.txt` | 26 | Hairstyles |
| `eye_color.txt` | 16 | Eye colors |
| `clothing_style.txt` | 26 | Fashion genres/eras (streetwear, gothic, dark academia...) |
| `expression.txt` | 24 | Facial expressions |
| `art_style.txt` | 30 | Art movements/styles (impressionism, cyberpunk, ukiyo-e...) |
| `art_medium.txt` | 26 | Art mediums (oil painting, claymation, vector art...) |
| `camera_shot.txt` | 22 | Shot framing (close-up, wide shot, bird's eye view...) |
| `camera_angle.txt` | 14 | Camera angle (low angle, dutch angle, overhead...) |
| `lighting.txt` | 26 | Lighting setups (golden hour, rim lighting, chiaroscuro...) |
| `composition.txt` | 16 | Composition/framing rules (rule of thirds, leading lines...) |
| `background.txt` | 30 | Generic settings/backdrops |
| `weather.txt` | 16 | Weather conditions |
| `time_of_day.txt` | 10 | Time of day |
| `season.txt` | 8 | Season |
| `aesthetic.txt` | 26 | Broad genre/aesthetic (cottagecore, noir, solarpunk...) |
| `mood.txt` | 22 | Overall mood/atmosphere |
| `flower.txt` | 20 | Flowers |
| `gemstone.txt` | 16 | Gemstones |
| `animal.txt` | 28 | Animals, real and mythical |

Example usage in a prompt box:

```
a woman with <wildcard:general/hair_color> <wildcard:general/hair_style>, <wildcard:general/eye_color> eyes, wearing <wildcard:general/clothing_style>, <wildcard:general/art_medium>, <wildcard:general/lighting>, <wildcard:general/camera_shot>
```

## Why 24 files / ~535 options, and not more?

Community "mega packs" (Civitai's wildcard collections, the various GitHub `sd-wildcards`
repos) run into the hundreds of files and tens of thousands of options, but that's because
they're built for one specific niche each (a costume pack, a creature pack, a fandom pack,
NSFW packs, etc.) and stacked together over years. For a **default, generic set everyone on
the server gets by default**, that approach has real costs:

- It clutters the `<wildcard:` autocomplete/search in the prompt box with dozens of
  near-duplicate or single-use-case files.
- Thin, oddly-specific files (10 fantasy-race names, 8 shoe brands) rot unmaintained and
  nobody remembers they exist.
- Broad ≠ deep: a general pack should cover *categories* well, not exhaustively enumerate
  every possible value in each category.

**~20-30 files in the 15-45-options-each range is the sweet spot** for a default pack: enough
variety that repeats aren't obvious in casual use, small enough that every file earns its
place and is easy to browse. This pack lands at 24/535, deliberately on the lower-file-count,
higher-quality-per-file end of that range.

Recommendation going forward: keep `general/` as the lean, curated default everyone gets, and
add **new, separately-named sibling folders** for anything niche or theme-specific (e.g.
`fantasy/`, `anime/`, `photography-gear/`, an 18+ pack, a specific game's cast) rather than
dumping more files into `general/`. That way users can reason about what `general/` contains
at a glance, and niche packs are opt-in by folder rather than diluting the default.

## Format notes / gotchas

- Files are plain UTF-8 text, no BOM, `\n` line endings, no trailing comments — kept as clean
  as possible since these are meant to be copied as-is into a live server.
- Entries are lowercase, comma-prompt-ready phrases (1-6 words), matching how most SD prompts
  are written and how the wildcard is typically substituted directly into a tag list.
- No duplicate lines within a file, no file under 5 options (validated with a script that
  mirrors SwarmUI's own `WildcardsHelper` parsing logic before this README was written).
