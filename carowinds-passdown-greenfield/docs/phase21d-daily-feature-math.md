# Phase 21D — Daily Feature math symbols and LaTeX-style rendering

Development phase after RC.5 (`0.1.0-rc.5`), following Phases 21A–21C. Adds mathematical notation to
the **Daily Feature only**: a compact symbol picker, a few equation templates, a small LaTeX-style
syntax, a live preview in Associate Passdown, and rendered formulas in the Classic, Compact and Modern
PDFs. It is a targeted enhancement, not a rich-text editor.

Schema versions are unchanged: **DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1** — no migration.
The next packaged candidate is still `0.1.0-rc.6`; `Directory.Build.props` stays at `0.1.0-rc.5`.
RC.6 was **not** packaged in this phase.

## 1. Feature scope

- Only the Associate → Overview **Daily Feature** text gets the picker, the preview, math-aware spell
  checking and formula rendering in PDFs. Operational Notes, ERT notes, persistent notes, product
  descriptions, ride lists, pricing and calendar entries are unchanged.
- The three Daily Feature types (Question / Useless Fact / Quote of the Day) keep their meaning.
- The stored value is still the plain, editable source text. The preview and PDFs interpret it; nothing
  rendered is ever stored, cached on disk or synced.
- Ordinary text stays ordinary: without an explicit `\(` or `\[` there is no preview, no visual change,
  and the PDF prints exactly the same italic text as before. `$`, `\`, `_`, `^` and Unicode symbols are
  never treated as math on their own.

## 2. Input delimiters

| Write | Meaning |
|---|---|
| `\( … \)` | Inline formula inside the sentence |
| `\[ … \]` | Display formula on its own centered line (may span several lines of source) |
| `\\(` | A doubled backslash never opens math (it is printed as typed) |

Several formulas can be mixed with prose. An opener without a matching closer — or with the other
kind of closer, or a nested opener — is not math: that text stays as typed and the preview says so.

## 3. Supported notation (the deliberate subset)

| Area | Supported |
|---|---|
| Scripts | `x^2`, `x_i`, `x_i^2`, `x^{n+1}`, primes `f'(x)` |
| Fractions | `\frac{a}{b}`, `\frac13`, `\dfrac`, `\tfrac`, nested fractions; `\binom{n}{k}` |
| Roots | `\sqrt{x}`, `\sqrt[n]{x}` |
| Large operators | `\int \iint \iiint \oint` (side limits), `\sum \prod \coprod \bigcup \bigcap` (stacked limits in display), `\limits` / `\nolimits` |
| Functions | `\sin \cos \tan \sec \csc \cot \arcsin \arccos \arctan \sinh \cosh \tanh \log \ln \exp \deg \arg \dim \ker`, and `\lim \max \min \sup \inf \det \gcd` (stacked limits in display); `\operatorname{name}` |
| Derivatives | as fractions: `\frac{d}{dx}`, `\frac{\partial f}{\partial x}`, `\nabla` |
| Greek | `\alpha … \omega` (italic), `\Gamma \Delta \Theta \Lambda \Xi \Pi \Sigma \Upsilon \Phi \Psi \Omega`, variants (`\varepsilon`, `\varphi` …) |
| Operators | `+ − * \pm \mp \times \div \cdot \ast \star \bullet \circ \cup \cap \setminus \land \lor \oplus \otimes \odot` |
| Relations / arrows | `= < > \le \ge \ne \approx \equiv \sim \simeq \cong \propto \ll \gg \in \notin \ni \subset \subseteq \supset \supseteq \perp \parallel \mid \to \rightarrow \leftarrow \leftrightarrow \Rightarrow \Leftarrow \Leftrightarrow \implies \iff \mapsto …` |
| Symbols | `\infty \partial \nabla \emptyset \forall \exists \neg \angle \hbar \ell \degree \ldots \cdots \therefore \because` |
| Delimiters | `( ) [ ] \{ \} | \|`, `\langle \rangle \lfloor \rfloor \lceil \rceil`; growing `\left( … \right)` (incl. `\left.`) |
| Spacing | `\, \: \; \! \ ` `\quad \qquad` `~` |
| Text / fonts | `\text{…}`, `\mathrm{…}`, `\mathbf{…}`, `\mathbb{R}`, `\mathit{…}` |
| Accents | `\hat \bar \vec \dot \ddot \tilde \overline \underline` |
| Matrices | `matrix`, `pmatrix`, `bmatrix`, `vmatrix`, `Bmatrix` (≤ 10×10), rows `\\`, cells `&` |

Unicode math works anywhere, with or without delimiters (e.g. `∫₀¹ x² dx = ⅓`).

**Not supported** (the formula is shown as typed, with a preview warning): any other command, user
macros (`\def`, `\newcommand`), packages, environments other than the matrices above (`align`,
`cases`, `array` …), `\\` line breaks outside a matrix, `\displaystyle`, colors, sizes, `\color`,
`\href`, `\includegraphics`, `\input`, and anything that would read files or URLs. This is not a full
LaTeX implementation and is not advertised as one.

## 4. Symbol picker

A small quiet "∑ Math" button under the Daily Feature editor (same outlined style family as the
Phase 21B toolbar) opens a compact popover, built only when first opened:

| Category | Symbols |
|---|---|
| Calculus | ∫ ∬ ∮ ∂ ∇ ∞ Δ ′ |
| Greek | α β γ δ ε θ λ μ π σ φ ω Σ Π Ω |
| Operators | ± × ÷ · − ≈ ≠ ≤ ≥ ≡ ∝ ∑ ∏ √ |
| Sets and logic | ∈ ∉ ⊂ ⊆ ∪ ∩ ∅ ∀ ∃ ¬ ⇒ ⇔ |
| Other | ° → ← ↔ ∴ ∵ ² ³ ⁿ ₀ ₁ ½ ⅓ ℝ |

Insertion behaviour:

- The symbol goes in **at the caret**; if text is selected, the **selection is replaced** (the
  conventional typing behaviour). It is never appended to the end of the field.
- Insertion is performed through the TextBox's own text-input path, exactly like typing: one undoable
  step (Ctrl+Z / Ctrl+Y), read-only respected, and the two-way `DailyFeatureText` binding sees an
  ordinary edit, so the dirty state and the existing autosave debounce apply unchanged.
- The picker's buttons never take focus and the editor's caret/selection is remembered while the
  editor has focus, so opening the popover never loses the insertion point. After an insertion the
  popover closes and focus returns to the editor.

## 5. Equation templates

Inline math `\(x\)`, display equation `\[x\]`, fraction `\(\frac{a}{b}\)`, derivative
`\(\frac{d}{dx}f(x)\)`, partial derivative `\(\frac{\partial f}{\partial x}\)`, integral
`\(\int_a^b f(x)\,dx\)`, summation `\(\sum_{i=1}^{n} i\)`, square root `\(\sqrt{x}\)`, nth root
`\(\sqrt[n]{x}\)`, limit `\(\lim_{x\to 0}f(x)\)`, power `\(x^{2}\)`, subscript `\(x_{i}\)`.
Each inserts plain editable source and **selects its first placeholder** (`a`, `f(x)`, `n` …) so typing
replaces it. There is no visual equation composer.

## 6. Rendering architecture

One interpretation pipeline, shared by the preview and all three PDF templates:

| Layer | Type | Role |
|---|---|---|
| Application | `DailyFeatureMath.DailyFeatureMathText` | Finds `\( \)` / `\[ \]` regions; limits; spell-check mask |
| Application | `DailyFeatureMath.MathParser`, `MathSymbols`, `MathNode` | The closed LaTeX-style subset → a node tree, or a `MathIssue` (never throws) |
| Documents | `DailyFeatureMath.TrueTypeFont` | Minimal read-only TrueType reader (cmap 4/12, hmtx, glyf incl. composites) for the bundled fonts |
| Documents | `DailyFeatureMath.MathLayoutEngine`, `MathBox` | TeX-style layout: atom spacing classes, script/script-script sizes, fractions on the math axis, stacked/side limits, radicals, growing delimiters, accents, matrices |
| Documents | `DailyFeatureMath.MathParagraphRenderer` | Mixed prose + math line breaking (inline formulas break only after a top-level operator/relation; display formulas centered; anything too wide or too tall is scaled, never clipped); output = per-line vector paths; bounded cache; `ToSvg` |
| Documents | `DailyFeatureMath.DailyFeaturePdfText` | The single Daily Feature body composer used by Classic, Compact and Modern |
| Desktop | `Controls.MathSymbolPicker`, `TextBoxInsertion` | Picker and typing-path insertion |
| Desktop | `Controls.DailyFeatureMathPreview`, `MathParagraphView` | Coalesced live preview drawing the same paths as Avalonia geometry |
| Desktop | `Spelling.SpellCheck.ExcludeMath` | Math-aware spell checking for the Daily Feature editor only |

Output is **pure vector geometry**: glyph outlines and rules become one non-zero-filled path per line,
radical signs are round-capped strokes. Avalonia draws them as `StreamGeometry`; QuestPDF draws each
line as a tiny SVG containing only `<path>` elements (no text, fonts, images, scripts or references).
Plain Daily Features never enter this pipeline.

Options considered: CSharpMath (MIT; full TeX parser, Typography-based) — its Avalonia package targets
older Avalonia and there is no QuestPDF backend, and it brings far more LaTeX surface than needed;
a browser/MathJax/KaTeX pipeline — rejected (needs a browser engine or JS runtime); external
TeX/LaTeX — rejected (external programs, large install). A small in-house parser + layout over a
bundled OFL math font was chosen for Avalonia 12 + QuestPDF compatibility, offline Windows x64
packaging, vector output, a controlled attack surface and long-term maintainability (≈2,000 lines,
no new NuGet packages).

## 7. Renderer dependency and license decisions

- **No new NuGet packages, no native code, no external programs, no network.**
- **STIX Two Math 2.13 b171** (`STIXTwoMath-Regular.ttf`, from `https://github.com/stipub/stixfonts`
  tag `v2.13b171`, SHA-256 `49b22767985171bce579307b21d98f4d35012fa39e316acc4ffcab8644dfb69b`) —
  **SIL Open Font License 1.1**; redistribution with software is permitted; the font is embedded
  unmodified and not sold by itself. Copyright and Reserved Font Name ("TM Math") recorded in
  `packaging/windows/THIRD-PARTY-NOTICES.txt` §8; full license in
  `packaging/windows/licenses/STIXTwoMath-OFL.txt` (added to `release-content-rules.json`
  `requiredFiles`; `RepositoryNoticeSources_MatchTheRequiredSet_AndAreComplete` passes) and next to
  the font in source.
- Prose inside math paragraphs reuses the **Lato** faces already embedded for PDFs (OFL 1.1, already
  noticed).
- The pre-existing **QuestPDF distribution-license decision remains unresolved** (Community license
  set in code; see `docs/release-strategy.md`). This phase does not change it and does not represent
  it as legally cleared. The GitHub Student Developer Pack is not assumed to resolve third-party
  commercial licensing.

## 8. Font behaviour

- Formulas use STIX Two Math, embedded in `Carowinds.Passdown.Documents.dll` (logical resource
  `DailyFeatureMath.STIXTwoMath-Regular.ttf`; +1.5 MB to that assembly). Variables use the Unicode
  Mathematical Italic letters, `\mathbb`/`\mathbf` the matching alphanumeric blocks.
- The same embedded font is registered with QuestPDF as a **glyph fallback** for plain-text Daily
  Features (`FontFamily("Lato", "STIX Two Math")`), so Unicode symbols such as ∫ ∑ ≤ ∇ ⇒ never print as
  missing-glyph boxes and never depend on fonts installed on the PC. Lato-covered text is unchanged.
- No LaTeX fonts, system fonts or downloads are used by the preview or PDFs.
- The **editor** (an ordinary TextBox) shows the raw source in the app font; symbols Inter lacks come
  from Windows' own font fallback (Segoe UI Symbol / Cambria Math ship with Windows 11). The font file
  is not duplicated into the Desktop assembly.
- The preview uses upright Lato for prose (easier to read on screen); the PDFs keep each template's
  italic Daily Feature style. Formula glyphs are identical in both.

## 9. Error fallback

- Parser problems (unsupported command, unbalanced braces, malformed scripts, too long/deep/large)
  produce a `MathIssue`; the region is printed **as its literal source** (delimiters included) in the
  surrounding prose font — never omitted and never "repaired" into a different formula. Other regions
  still render.
- Unclosed openers are ordinary text.
- Preview: one quiet warning line, e.g. "Shown as typed: \foo isn't supported." — no exception text,
  the editor text is never changed, autosave is unaffected.
- If the bundled fonts cannot be loaded or layout fails unexpectedly, the preview says math preview
  isn't available and **PDFs print the plain text** exactly as before. Editing, autosave, date
  navigation, finalization, Teams Copy and sync never depend on the renderer.

## 10. Spell-check integration

`spell:SpellCheck.ExcludeMath="True"` is set on the Daily Feature editor only. Before checking, every
closed math region (delimiters included) is replaced by spaces (`MaskMathForSpellCheck`), so LaTeX
commands (`\frac`, `\partial`, `\sqrt`, `\sum`) and variable names are never underlined, while prose
before, between and after formulas is checked at its real offsets (underlines line up). Unclosed
openers are prose. The domain allow-list, Ignore / Add to dictionary and the Phase 20C lifecycle
(underlines removed when navigating away, pending checks cancelled, re-check on return, no
window-wide overlay) are unchanged — the picker and preview add no adorners.

## 11. PDF behaviour

- Classic (10 pt, line height 1.2), Compact (8.25 pt, natural), Modern (10.5 pt, 1.25) — the same
  source means the same formula; only size/line height differ. Math is added to no other section.
- Empty / whitespace-only Daily Feature: still omitted entirely (no heading, rule or space).
- Plain text: the same QuestPDF italic text as before (only the glyph-fallback family is added).
- With math: the paragraph is laid out at the width QuestPDF offers and emitted line by line inside a
  paging component, so a long Daily Feature continues on the next page between lines (the existing
  section `PreventPageBreak` still keeps short ones together). Display formulas get ~0.3 em above and
  below. No line is wider than the column or taller than ~24 body lines (taller formulas are scaled),
  so nothing is clipped and a fresh page always accepts the next line.
- Formulas in the PDF are vector outlines: they are not selectable/searchable text, and the prose of
  a paragraph that contains math is drawn the same way (see Known limitations).
- Offline: nothing is downloaded or contacted; the output is deterministic (same model → same bytes).

Pagination results (`DailyFeatureMathPdfTests`, NormalDay fixture): an inline-math sentence stays
one page in all templates and moves the next heading by ≤ 8 pt versus equivalent plain text; a
display integral plus derivative, partial, summation, nested fraction and nth root stays one page; a
busy passdown with a selected show and math stays one page; an 80-term inline sum stays one page
(wrapped at operators); a 30-line Daily Feature with 60 formulas paginates within 1–6 pages without
failures.

## 12. Draft compatibility

No draft format change: `DailyFeatureText` is the same string field (Draft 4). Autosave, restart,
date switching, reload and local edits keep the exact source (`DailyFeatureMathPersistenceTests`).
**Older application versions** (RC.5 and earlier) show and print the raw source text — e.g.
`\(\frac{1}{2}\)` appears literally in their editor and PDF — but never alter or lose it; they have
no renderer, so backward rendering compatibility is not claimed.

## 13. Finalized History

Snapshots store the source text unchanged (Snapshot 2); the frozen PDF stored at finalization holds
the rendered formulas as vector paths and is never regenerated, so History keeps its appearance.
Existing snapshots and frozen PDFs are not rewritten. History's detail panel shows the stored source
text (as before). A finalization projection's PDF is byte-identical to a live render of the same model.

## 14. Personal Sync

Math travels inside the existing synced draft text — no new sync type, no rendered images, no caches.
Conflict handling is unchanged. A receiving PC on this version renders the same source identically
(the fonts are embedded); a PC on an older version shows the source text (§12).

## 15. Teams Copy exclusion

Teams Copy never included the Daily Feature; that is unchanged. The three outputs (Operations
Summary, Today's Pricing, Operations + Pricing) contain no LaTeX, no math section and no shows, and
are byte-identical with or without math in the Daily Feature (`TeamsCopy_NeverIncludesTheDailyFeature_OrLatex`).

## 16. Performance and resource limits

| Limit | Value | Over the limit |
|---|---|---|
| Daily Feature length examined for math | 8,000 chars | whole text plain |
| Formulas formatted per Daily Feature | 40 | the rest shown as typed |
| One formula's source | 600 chars | shown as typed |
| Nesting depth | 12 | shown as typed |
| Nodes per formula | 400 | shown as typed |
| Matrix | 10 × 10 | shown as typed |
| Formula height | ~24 body lines | scaled to fit |

Stored content is never truncated. The preview re-renders 250 ms after the last edit (cancellable;
plain text hides it immediately) and does no database or network work. Layouts are cached in memory
by (text, width, style) — at most 64 entries, LRU, rebuildable, never persisted or synced; glyph
outlines are cached per font glyph. 3,000 random malformed inputs parse in well under a second.

Diagnostics log only a status value (`UnsupportedCommand`, `MalformedExpression`,
`ResourceLimitExceeded`, `RendererUnavailable`, `RenderFailed`), once per kind per session, under the
`DailyFeatureMath` category — never the Daily Feature text, the formula or exception messages, so
exported diagnostics contain no user equations.

## 17. Test coverage

| Project | Tests | Covers |
|---|---|---|
| Application | `DailyFeatureMathTextTests`, `MathParserTests`, `DailyFeatureMathPersistenceTests` | delimiters, escapes, plain/Unicode text, inline/display/multiple, unclosed/mismatched, failed-region fallback, limits, spell-check masking (prose checked, LaTeX skipped, allow-list), supported syntax, unsupported/malformed kinds (incl. `\input`, `\def`, `\write18`, URLs), nesting/length/node/matrix limits, 3,000-input fuzz; draft/snapshot round trips, sync validation, schema versions |
| Documents | `MathLayoutTests`, `MathParagraphRendererTests`, `DailyFeatureMathPdfTests` | fonts load offline; fraction/script/limit/integral/radical/nth-root/delimiter/matrix geometry; atom spacing; breakable pieces; line fitting at 80/160/540 pt; centered display; fallback; bounded cache; SVG is paths only; pathological speed; all three templates: drawn (no LaTeX in PDF text), compact spacing, plain text unchanged, Unicode fallback, malformed/unsupported, long/very long, busy day with shows, empty omitted, deterministic bytes, frozen projection, Teams Copy |
| Infrastructure | `PersonalSyncServiceTests.DailyFeatureMath_SyncsAsItsExactSource`; notices test | two-device sync of the exact source; STIX license in the release set |
| Ui | `Phase21DDailyFeatureMathUiTests` | caret insertion, selection replacement, no end-append, every template + placeholder selection, undo/redo, binding/dirty state, read-only, no text loss, catalog, preview hidden for plain text, coalescing, error warnings, hide on removal, measure, ExcludeMath, markup |
| Ui (real window, run alone) | `Phase21DMathEditorWindowTests` | focus restoration, remembered selection across focus loss, real preview delay, navigation/detach cancels pending preview work |

`Phase21DMathEditorWindowTests` is a fifth real-Window class and runs **alone** in the deterministic
partition (like `SpellCheckNavigationLifecycleTests`). Note: this test assembly must not use
`MemberData` (it changes xUnit discovery threads and breaks the real-Window classes) — the template
theory uses `InlineData`.

## 18. Windows validation checklist

**NOT EXECUTED** — no Windows host was available. Run on Windows 11 x64 with a backed-up profile:

Editor
- [ ] Plain Question/Fact/Quote text: no preview, no visual change, saves and prints as before.
- [ ] "∑ Math" opens the popover; each category inserts its symbol at the caret.
- [ ] With a word selected, a symbol replaces the selection.
- [ ] Undo/redo (Ctrl+Z / Ctrl+Y) after insertions.
- [ ] Focus returns to the editor after insertion; caret placement and template placeholder selection.
- [ ] Autosave indicator: Unsaved → Saved after an insertion; restart keeps the text.

Equation preview
- [ ] Integral, derivative, fraction, summation, mixed prose, multiple equations render correctly.
- [ ] Invalid expression (`\(\frac{1}{\)`, `\(\foo\)`) shows the quiet warning; typing stays responsive.
- [ ] Preview appears only with `\(`/`\[`; disappears when removed.

Spell checking
- [ ] Ordinary misspellings underlined around formulas; `\frac`, `\partial`, `\sqrt`, `\sum` and variables are not.
- [ ] Navigate away and back: underlines removed and re-checked; no overlay artifacts on other screens.

PDF
- [ ] Classic, Compact, Modern: same formulas; inline baseline alignment; display centered with balanced space.
- [ ] Long expressions wrap/scale without clipping; unreadable glyphs absent (no boxes).
- [ ] Busy passdown with selected shows + math; invalid expression shows its source text.
- [ ] Plain Unicode symbols (∫ ∑ ≤ ∇ ⇒) print correctly.

System
- [ ] Offline (network off): preview and PDF still render.
- [ ] Light/dark modes (preview follows the theme foreground).
- [ ] 100%, 125%, 150% scaling: picker, popover and preview crisp and unclipped.
- [ ] Windows font behaviour in the editor (symbols visible via Windows fallback).
- [ ] Restart/draft persistence; finalize a math passdown and reopen it in History.
- [ ] Personal Sync between two PCs on this version (where practical).

## 19. Known limitations

- Not full LaTeX (§3). Unsupported constructs are shown as typed, with a warning in the preview.
- Formulas — and the prose of a Daily Feature paragraph that contains math — are vector outlines in
  the PDF: not selectable, searchable or read by screen readers as text. Plain Daily Features remain
  real text.
- Delimiters grow by scaling the font's own glyph (not OpenType MATH size variants/extenders); very
  tall parentheses look slightly stretched. Large operators are scaled rather than using display
  variants. Kerning between prose glyphs inside math paragraphs is not applied.
- Inline fractions use TeX's text-style (small) parts, as LaTeX does; use `\dfrac` for a larger one.
- The editor itself shows source, not formatted math (by design).
- Older versions show source text only (§12).
- Windows validation not executed.
