# Phase 29C — Windows rich-text clipboard interoperability

Baseline `a612fae` (Phase 29B). Version stays `0.1.0-rc.9`. Nothing pushed, published or packaged as RC.10.
RC.8/RC.9 artifacts untouched.

**Windows status: NOT VALIDATED.** The diagnosis comes from Avalonia's shipped binaries and the Windows VM
findings below. It has not yet been confirmed by running the new OLE probe on Windows. Every Word and Teams
row in `tools/phase29c-windows-kit/PHASE29C-CHECKLIST.txt` is **NOT EXECUTED**.

## 1. Windows findings (product owner, Phase 29B build)

- The formatted preview shows bold correctly.
- Copy Formatted → Teams desktop: no bold. → Word: no bold.
- **Word's Paste Special does not offer "HTML Format".**
- The Phase 29A inspector (raw `GetClipboardData`) found "HTML Format" with valid CF_HTML offsets, UTF-8 and
  bold markup.
- Phase 29B (HTML before text, explicit `<b style>`) changed nothing.

The Paste Special finding is decisive. Word builds that list from the formats it can *obtain*. A format that a
raw clipboard read retrieves but Word does not list fails on the path Word uses, not in the HTML content. No
change to the markup can fix that.

## 2. Diagnosis

Avalonia 12.1.1 `Avalonia.Win32.dll`, decompiled with ILSpy 11.1 (`ClipboardImpl`,
`DataTransferToOleDataObjectWrapper`, `OleDataObjectHelper`, `ClipboardFormatRegistry`):

| Step | What Avalonia does |
| --- | --- |
| Registration | `RegisterClipboardFormat("HTML Format")` for a bytes platform format. The ID is the standard one, so registration is fine |
| Publication | `ClipboardImpl.SetDataAsync` → `OleSetClipboard(wrapper)`. It is **not flushed** (`OleFlushClipboard` runs only on an explicit `FlushAsync`). The clipboard holds Avalonia's live `IDataObject`, with OLE's hidden `CLIPBRDWNDCLASS` window as owner |
| Data | `WriteBytesToHGlobal` copies the bytes unchanged into a zero-filled `GHND` block, so the CF_HTML bytes are correct |
| **Request validation** | `ValidateFormat` (used by `QueryGetData`, `GetData` and `GetDataHere`): `if (format->tymed != TYMED_HGLOBAL && (format->tymed != TYMED_GDI \|\| cfFormat != CF_BITMAP)) return DV_E_TYMED;` |

`FORMATETC.tymed` is a **bit mask** under the OLE contract: a consumer asks for any medium it can accept,
for example `TYMED_HGLOBAL | TYMED_ISTREAM`. Avalonia compares it for *equality* with `TYMED_HGLOBAL`, so
every combined-mask request is refused with `DV_E_TYMED`.

Consequences:

- **Raw Win32 readers** (`OpenClipboard` + `GetClipboardData`: the inspector, and Chromium's own clipboard
  code) get the data. When they ask, OLE's clipboard window renders the delayed format by calling
  `GetData` with the exact enumerated `FORMATETC` (`TYMED_HGLOBAL`), which passes. That is why every
  structural check passed.
- **OLE consumers** (`OleGetClipboard` + `QueryGetData`/`GetData` with a mask) are refused for "HTML Format".
  Office/Word read the clipboard through OLE, which matches Paste Special not listing HTML. Plain text still
  pasted because Windows synthesizes CF_TEXT/CF_OEMTEXT from CF_UNICODETEXT, and OLE serves synthesized
  formats from the system clipboard itself rather than from Avalonia's object. Bullets "survived" only as the
  `• ` characters in that plain text.
- **Teams desktop** behaves the same. That is consistent with its paste reading through OLE, but this is
  inferred, not proven. Teams in Edge is a separate row on the checklist.

## 3. Fix: native Windows clipboard writer

New `Infrastructure/Clipboard/WindowsNativeClipboard` (`[SupportedOSPlatform("windows")]`):

1. `RegisterClipboardFormatW("HTML Format")`.
2. `OpenClipboard(mainWindowHandle)`: up to 10 attempts, 50 ms apart. The owner is the app's top-level window
   (`TopLevel.TryGetPlatformHandle()`), never NULL.
3. `EmptyClipboard()`.
4. `SetClipboardData("HTML Format", block)`: the unchanged CF_HTML bytes plus one NUL after EndHTML
   (`WindowsClipboardPayload.Html`).
5. `SetClipboardData(CF_UNICODETEXT, block)`: UTF-16LE plus a UTF-16 NUL (`WindowsClipboardPayload.UnicodeText`).
6. `CloseClipboard()` in `finally`. A block the system did not take is freed. Any failure throws `Win32Exception`.

Both formats are rendered immediately and owned by the system, so Windows' own OLE clipboard object serves
OLE consumers with full `tymed` support. The data also survives the app closing.

`AvaloniaClipboardService.SetTextAndHtmlAsync` uses the native writer on Windows and Avalonia's `DataTransfer`
elsewhere (macOS/Linux development only, unchanged). Plain-text copy and Copy Image still use Avalonia; Word
already pasted plain text correctly. If the native write throws, `TeamsClipboard` falls back to plain text
through the separate Avalonia `SetTextAsync` path, which existing tests cover.

**Rejected alternative:** calling Avalonia's `clipboard.FlushAsync()` after `SetDataAsync` (`OleFlushClipboard`
renders every format and drops Avalonia's object). It would probably work too, but it still depends on
Avalonia's rendering. It adds a 10 ms delay plus flush retries, and it would change behaviour for every Avalonia
clipboard user in the app. The native writer is about 100 lines, isolated, and its exact bytes are pinned by
tests.

**RTF:** not added. The evidence points to the delivery path, not to Word or Teams needing RTF. Word consumes
CF_HTML once it can obtain it. RTF would be a second rich representation to keep consistent, with no evidence
that it is needed.

**HTML styling:** unchanged from Phase 29B. That change was not the root cause, but `<b style="font-weight:bold">`
remains valid, safe and portable, so it was kept rather than churned again.

## 4. Diagnostics

`tools/phase29a-windows-kit/scripts/Inspect-TeamsClipboard.ps1` gained section **3b, OLE consumer probe**. On an
STA thread it calls `OleGetClipboard` and reports:

- the clipboard owner's window class and process (`CLIPBRDWNDCLASS` = an OLE data object is on the clipboard);
- the OLE `EnumFormatEtc` list with tymed;
- `QueryGetData` for "HTML Format" and CF_UNICODETEXT with `TYMED_HGLOBAL`, `TYMED_HGLOBAL|TYMED_ISTREAM` and
  all TYMEDs, as HRESULTs;
- `GetData(HGLOBAL|ISTREAM)` for both formats, and whether the OLE-delivered HTML bytes equal the raw
  CF_HTML up to EndHTML.

PASS rows: "OLE consumer can query HTML Format (TYMED_HGLOBAL|TYMED_ISTREAM)", "… (any TYMED)",
"OLE consumer can retrieve HTML Format (GetData)", "OLE consumer can retrieve CF_UNICODETEXT (GetData)",
"OLE HTML bytes match the raw CF_HTML (up to EndHTML)". Checklist step 0 runs it against the old build to
confirm the diagnosis, and step 1.3 runs it against the new build.

The probe's C# was compile-checked on the build host at C# language version 5 (what Windows PowerShell 5.1's
`Add-Type` uses). The script parses without errors, is ASCII-only, and still passes every check in saved-file
mode. The probe itself only runs on Windows.

## 5. Tests (Mac-auto)

- Infrastructure `Clipboard/WindowsClipboardPayloadTests` (4): CF_UNICODETEXT bytes are UTF-16LE plus a 2-byte
  terminator; empty text is just the terminator; the HTML block is the CF_HTML bytes unchanged plus one NUL, with
  no BOM; the writer is Windows-only and uses "HTML Format".
- Ui `MorningMessageViewModelTests.Phase29C_*` (4): formatted copy goes to the native writer with the owner
  handle, the exact plain text and `CfHtml.Build(fragment)` (round-trips to the fragment); a writer failure
  propagates, so the caller falls back to plain text; with no owner window nothing is written; the default
  service uses the native writer only on Windows.
- All Phase 29B tests are unchanged: undated heading, plain/rich equivalence, bold semantics, bullets, escaping,
  OD Notes exclusion, existing Copy plain text byte-identical.

The Win32 calls themselves cannot run on macOS; they are covered by the Windows checklist.

Deterministic suite (Release, `Category!=LiveSmoke`, five real-Window classes alone): 303 Domain + 1330
Application + 1140 Infrastructure + 538 Documents + 1515 Ui mixed + 26 + 113 + 2 + 7 + 4 = **4978 passed,
0 failed** (Phase 29B baseline 4970; +4 Infrastructure, +4 Ui). Clean Release build
(`dotnet build CarowindsPassdown.slnx -c Release --no-incremental` after `dotnet clean`): **0 warnings, 0 errors**.

## 6. Not changed

Phase 30 sync, OD Notes separation, the three original Copy options' content, PDF, History, DB 17 / Draft 4 /
Snapshot 2 / Sync 1, app version, updater, release artifacts.
