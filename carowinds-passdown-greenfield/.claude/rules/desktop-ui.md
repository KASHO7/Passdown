---
paths:
  - "src/Carowinds.Passdown.Desktop/**"
  - "tests/Carowinds.Passdown.Ui.Tests/**"
  - "tools/visual-fixtures/**"
---

# Desktop presentation

Use approved Avalonia design direction, shared tokens/styles, existing view models and commands. Preserve native Windows chrome, original icon, navigation/workspace state, light/dark/system appearance, local animation preference and OS reduced motion. Preserve accessibility names, focus, keyboard behavior, status semantics, responsive layout and DPI support. Animations cannot delay commands, editing, saves, navigation or preview. Approved prototypes are visual references only; never import sample data or invent missing approved designs. Do not remove functionality for visual simplicity. Separate presentation from business rules, avoid duplicate state/save paths, and consolidate repeated styles when justified. Use existing UI polish skill and visual fixtures. Headless renders are not Windows DPI/accessibility evidence.
