# DevForge Studio UI/UX Redesign

**Date:** 2026-08-26

**Status:** Approved for implementation planning

**Scope:** Native WPF presentation redesign for the existing DevForge Studio workflows

## 1. Context

DevForge Studio has production application logic and a complete native WPF workflow, but its presentation remains a functional baseline. The supplied screenshots show weak hierarchy, raw platform controls, inconsistent contrast, oversized empty regions, missing empty-state guidance, and form/table layouts that do not communicate state or next actions clearly.

The authoritative implementation is the `codex/m4-m11-completion` worktree. It already contains the Dashboard, Create Project, Plan Preview, Execution Center, Local Ready/Completed, Run History, Blueprint Catalog, Environment Doctor, Settings, navigation shell, themes, notifications, and their ViewModels and commands. The redesign must preserve those workflow and security boundaries.

## 2. Product experience goal

Make DevForge feel like a trustworthy, premium Windows developer tool: focused enough for repeated daily use, expressive enough to have its own identity, and explicit about every risky or recoverable state.

The main user journey must read as one coherent progression:

`Configure -> Review -> Execute -> Local Ready -> Completed or Publish Pending`

At every stage the interface must answer:

1. Where am I?
2. What is the current state?
3. What needs my attention?
4. What is the safest next action?

## 3. Goals

- Create a cohesive Developer Studio visual system for light, dark, and system themes.
- Redesign all existing Desktop views, not only the startup shell.
- Preserve current ViewModel, command, navigation, persistence, process, file-system, Git, GitHub, and safe-mode behavior.
- Make empty, loading, validation, error, stale, disabled, running, success, cancelled, and recovery states deliberate.
- Improve scanability for technical data without hiding evidence or remediation.
- Support the existing 960 x 640 minimum and remain polished at 1280 x 800 and larger.
- Preserve keyboard navigation, automation names, screen-reader semantics, and high-contrast legibility.
- Avoid heavy third-party UI frameworks and retain a native WPF application.

## 4. Non-goals

- No change to project-generation, execution, recovery, publication, persistence, or security semantics.
- No new cloud, telemetry, embedded browser, AI, arbitrary shell, or administrator behavior.
- No navigation destination without working application logic.
- No speculative search, filters, browse dialogs, analytics, or actions that are not backed by existing commands.
- No rewrite to WinUI, Avalonia, web UI, Electron, Tauri, or Blazor Hybrid.
- No decorative animation that delays work or obscures execution state.

## 5. Design direction

### 5.1 Selected approach

Use a modern **Developer Studio** direction: dark-first graphite surfaces, electric indigo as the primary accent, restrained violet highlights, subtle depth, and compact information-rich composition. Light theme uses the same semantic hierarchy rather than being a color inversion.

This direction was selected over:

1. A strict Fluent 2 clone, which is familiar but gives DevForge too little identity.
2. A high-glass creative studio, which is visually distinctive but adds noise to dense execution and diagnostics screens.

### 5.2 Visual principles

- **State before decoration:** progress, trust, risk, errors, and next actions receive the strongest hierarchy.
- **Calm surfaces:** depth comes primarily from tone and borders; shadows and glow remain secondary.
- **Progressive disclosure:** summaries stay visible while technical detail moves into expandable or scrollable regions.
- **Consistent action hierarchy:** one primary action per context; destructive or recovery actions never visually compete with it.
- **Native clarity:** Segoe UI Variable and Segoe Fluent Icons provide a Windows-native feel without imitating another product.

## 6. Information architecture and shell

### 6.1 Shell layout

- Use a 248 px navigation rail at normal widths and a compact icon-focused rail at the minimum width.
- Place product identity at the top, primary routes in the middle, and a compact environment/status area near the bottom.
- Every route has a Fluent icon, label, selected state, tooltip, automation name, and disabled explanation.
- Selected navigation uses an accent-tinted surface plus a 3 px indicator. Hover and pressed states remain distinct.
- The content region owns safe-mode banners, page content, and bounded toast notifications.
- Safe mode is a persistent warning callout with icon, heading, explanatory copy, and non-alarming amber semantics.

### 6.2 Page frame

Every page uses a consistent structure:

1. Optional eyebrow or workflow context.
2. Page title and one-sentence purpose.
3. Page-level actions aligned to the right.
4. Content with a 24-32 px outer gutter.
5. Optional sticky action bar for long forms or irreversible transitions.

## 7. Design tokens

### 7.1 Spacing and geometry

| Token role | Values |
| --- | --- |
| Spacing scale | 4, 8, 12, 16, 20, 24, 32, 40, 48 |
| Standard control height | 40 px |
| Prominent control height | 44 px |
| Compact control height | 32 px |
| Card radius | 12 px |
| Prominent surface radius | 16 px |
| Small badge radius | 999 px pill or 6 px tag |
| Standard page gutter | 28 px, reduced to 20 px at minimum width |

### 7.2 Typography

Use `Segoe UI Variable Text` with a `Segoe UI` fallback.

| Role | Size / weight |
| --- | --- |
| Display | 36 / SemiBold |
| Page title | 28 / SemiBold |
| Section title | 20 / SemiBold |
| Card title | 16 / SemiBold |
| Body | 14 / Regular |
| Label | 13 / SemiBold |
| Caption | 12 / Regular |
| Monospace evidence | 13 / `Cascadia Mono`, fallback `Consolas` |

### 7.3 Semantic color system

Dark theme base:

- App background: `#0B0E14`
- Navigation: `#0D111A`
- Surface: `#121722`
- Raised surface: `#191F2D`
- Border: `#283043`
- Primary text: `#F4F7FB`
- Secondary text: `#9DA8BB`
- Primary accent: `#6C7CFF`
- Accent hover: `#7E8BFF`
- Violet accent: `#8B5CF6`

Light theme base:

- App background: `#F5F7FB`
- Navigation: `#FFFFFF`
- Surface: `#FFFFFF`
- Raised surface: `#F8FAFD`
- Border: `#DCE2EC`
- Primary text: `#172033`
- Secondary text: `#657086`
- Primary accent: `#4F5FE7`
- Accent hover: `#4050D6`
- Violet accent: `#7651D6`

Semantic status colors use independent surface, border, foreground, and icon tokens for Info, Success, Warning, Error, and Neutral. Status must never be conveyed by color alone.

## 8. Resource and component architecture

### 8.1 Resource dictionaries

Split presentation resources by responsibility:

- `Tokens.xaml`: spacing, radii, sizes, durations, typography scale.
- `Colors.Light.xaml` and `Colors.Dark.xaml`: semantic palette only.
- `Typography.xaml`: text styles and monospace evidence styles.
- `Icons.xaml`: named Segoe Fluent glyph resources.
- `Controls.xaml`: base control templates and interaction states.
- `Components.xaml`: cards, badges, callouts, form fields, empty states, action bars, stepper, timeline, toast, and console surfaces.
- `Animations.xaml`: short optional transitions and reduced-motion fallbacks.

Theme switching continues through `ThemeService` and dynamic resources. A theme dictionary must provide every semantic color token used by common resources.

### 8.2 Reusable presentation components

- `PageHeader`: context, title, description, and action slot.
- `Card`: normal, interactive, and emphasized variants.
- `StatusBadge`: neutral, info, success, warning, and error variants with icon and text.
- `Callout`: safe mode, stale cache, validation, failure, and recovery messaging.
- `EmptyState`: icon, concise explanation, and optional backed action.
- `FormField`: label, optional/required metadata, control slot, hint, and validation message.
- `ActionBar`: sticky page actions with primary/secondary/danger hierarchy.
- `WorkflowStepper`: Configure, Review, Execute, Complete.
- `TimelineItem`: execution step status, duration/label, error code, and remediation.
- `ConsolePanel`: bounded, virtualized progress output in a monospace style.
- `Toast`: bounded notification presentation using the existing notification service.

Implementation may use styles and DataTemplates instead of custom controls when that keeps bindings simpler. A small presentation-only panel or attached behavior is acceptable for adaptive layout; it must not own application state.

## 9. Screen designs

### 9.1 Dashboard

- Use a page header with the primary `Create Project` command.
- Present Recent Projects, Action Needed, Saved Presets, and Environment Health as a responsive card grid.
- Give Action Needed and unhealthy/stale environment states higher visual priority than passive content.
- Render purposeful empty states instead of bare sentences inside large cards.
- Keep project paths available through tooltips and trimming; never expose source content.

### 9.2 Create Project — Configure

- Show the workflow stepper above the page content.
- Group fields into Project, Blueprint Options, Git & GitHub, and Open After Generation cards.
- Keep field validation adjacent to the relevant control and retain a concise validation summary for page-level issues.
- Use clear dependency states for Git and GitHub controls. Disabled controls include explanatory text or tooltip.
- At wide sizes, show a compact configuration summary beside the main form. At minimum width, stack it below.
- Place `Review Plan` in a sticky action bar and make it the single primary action.

### 9.3 Create Project — Review Plan

- Replace the single long review card with a review page inside the same workflow.
- Lead with blueprint identity, trust badge, plan hash, target summary, and Git/GitHub intent.
- Present Artifacts, Dependencies, Tools, Steps, Validators, Effective Inputs, Features, and Warnings as well-labeled sections with bounded lists or expanders.
- Render plan hash and process previews with a monospace evidence style.
- Keep `Back` and `Create & Validate` in the sticky action bar.

### 9.4 Execution Center

- Lead with overall status, a bounded progress indication, and the backed `Cancel` action.
- Use a two-pane layout at normal widths: execution timeline on the left, structured progress output on the right.
- Stack the panes at the minimum layout only if needed to prevent unreadable columns.
- Each failed step shows error code and remediation next to that step.
- Contextual actions appear in a bottom action bar. Resume, Retry, and Cleanup retain existing command availability. Future disabled actions remain visibly unavailable and explain why.
- Output remains virtualized and redacted; the UI must not reconstruct or reveal secret-shaped data.

### 9.5 Local Ready, Completed, and Publish Pending

- Use a success or recovery hero that differentiates local generation state from publication state.
- Keep `Open IDE` as the primary action when available.
- Show target, blueprint, plan hash, elapsed time, finalization, and report state in a compact summary grid.
- Group Evidence, Warnings, Generation Reports, and Publication into separate cards.
- `PublishPending` uses a warning callout that explicitly says the validated local project remains safe, followed by `Retry Publish`.
- Publication and IDE errors show remediation without replacing the successful local-result message.

### 9.6 Blueprint Catalog

- Render each blueprint as a responsive card with identifier, version, trust badge, issue text, and backed `Create` action.
- Do not add search or filter controls until corresponding behavior exists.
- Use an explicit empty/loading/error state rather than an empty bordered list.

### 9.7 Run History

- Render virtualized run rows or cards with run identity, status badge, error code, and available recovery actions.
- Keep action hierarchy contextual: Retry Publish is visually distinct from execution retry; Cleanup is not primary.
- Use an explicit no-history state and retain Refresh as a secondary page action.
- Do not invent timestamps or blueprint metadata not exposed by the current item model.

### 9.8 Environment Doctor

- Use a header with secondary `Copy Diagnostics` and primary `Rescan` actions.
- Show last scan time and stale/failed cache state in a semantic callout.
- Restyle the virtualized tool list as a readable data surface with status badges, wrapping compatibility, and remediation.
- Preserve horizontal scrolling when the minimum width cannot display every technical column without truncating critical text.

### 9.9 Settings

- Use Getting Started, Project Defaults, and Appearance sections.
- Render onboarding readiness as a progress/checklist card rather than editable-looking raw checkboxes.
- Use consistent labels, hints, and validation placement.
- Keep Reset and Save in a sticky action bar; Save is primary.
- Preserve English and Vietnamese choices and System/Light/Dark theme behavior.

## 10. Adaptive layout

The application remains desktop-first with the existing 960 x 640 minimum.

- At 1200 px and above, use full navigation labels, two-column dashboards, and side-by-side execution panes.
- From 960 to 1199 px, reduce page gutters, allow card wrapping, compact the navigation presentation, and stack layouts that would otherwise clip.
- Long forms retain a bounded readable width rather than stretching fields across the full window.
- Lists and logs use the remaining height and virtualize their content.
- Sticky page actions remain reachable without scrolling to the end of long forms.
- Text wrapping is preferred for remediation and status copy; identifiers and paths trim with a tooltip where wrapping would reduce scanability.

WPF does not provide UWP-style adaptive triggers. The implementation must use wrapping panels, grid sizing, or a small tested presentation helper rather than brittle code-behind that mutates application state.

## 11. Interaction states and motion

Every interactive control must define default, pointer-over, pressed, focused, disabled, and validation states. Selected navigation and selected list items must be visible in both themes.

- Hover and focus transitions: 120 ms.
- Panel and toast entrance: 160-180 ms.
- Progress updates remain direct and do not animate text.
- No looping glow, decorative particle effects, large parallax, or delayed completion transitions.
- When reduced motion is requested by the operating system, nonessential transitions are disabled.

## 12. Accessibility

- Preserve or improve all current `AutomationProperties.Name` and HelpText values.
- Do not remove working keyboard access or command bindings when templating controls.
- Ensure visible focus indicators on every interactive control.
- Maintain at least WCAG AA contrast for normal text and meaningful non-text indicators.
- Pair status color with icon and text.
- Ensure disabled controls remain legible and expose the disabled reason.
- Use logical reading and tab order through navigation, header actions, content, and sticky action bars.
- Verify light, dark, and Windows high-contrast behavior; high contrast may simplify decorative effects.

## 13. Data flow and security boundaries

Views continue to bind to the existing ViewModels and commands. The redesign may introduce converters, templates, presentation-only state, and layout helpers, but it must not bypass existing application services.

The Desktop layer must not:

- Launch processes directly.
- Access the file system directly.
- Read EF Core or SQLite directly.
- Reveal sensitive process values or unredacted logs.
- Reimplement generation, retry, cleanup, or publication decisions.
- Enable commands that application state has disabled.

The existing route, stage, command, notification, safe-mode, theme, and automation contracts remain authoritative.

## 14. Error and recovery presentation

- Inline validation belongs next to the field and may be summarized at the page level.
- Page-loading failures use an error state with a backed retry or refresh command where one exists.
- Safe mode uses a persistent warning callout and never visually suggests that mutating actions are available.
- Environment stale and scan-failed states clearly distinguish cached data from fresh data.
- Execution errors remain attached to their failing step and include error code and remediation.
- `LocalReady` stays visually successful even when publication or IDE handoff fails.
- `PublishPending` communicates recoverability and the continued safety of the local project.
- Empty states explain why the area is empty and only offer actions backed by existing commands.

## 15. Implementation sequence

1. Add token, palette, typography, icon, control, and component resources.
2. Restyle the shell, navigation, safe-mode banner, and notifications.
3. Redesign Dashboard, Blueprint Catalog, Run History, Environment Doctor, and Settings.
4. Redesign the full Create Project workflow, Execution Center, and Local Ready/Completed states.
5. Add only the minimum presentation helper needed for adaptive behavior.
6. Close accessibility, theme, clipping, empty/loading/error-state, and visual QA gaps.

No screen is considered complete if it only inherits new colors while retaining the baseline information hierarchy.

## 16. Verification strategy

### 16.1 Automated verification

- Load all application and theme resource dictionaries without XAML parse failures.
- Verify light and dark palettes supply all semantic resources consumed by common styles.
- Verify reusable templates retain required commands, bindings, automation names, and focus behavior.
- Keep existing Desktop ViewModel, navigation, theme, startup, publication, creation, execution, accessibility, and behavior tests green.
- Add focused presentation tests for any new converter, adaptive panel, or component behavior.
- Run locked restore, formatting verification, Release build, focused Desktop E2E tests, and the full relevant test suite.
- Run `git diff --check` and static boundary scans required by the repository.

### 16.2 Visual verification

Render or capture these states in both light and dark themes at 1280 x 800 and 960 x 640 where the data fixture permits:

- Dashboard with empty/default data.
- Create Project Configure and Review Plan.
- Execution Center running and failed/recovery states.
- Local Ready/Completed and Publish Pending.
- Empty and populated Blueprint Catalog and Run History.
- Environment Doctor normal, stale, and failed-cache states.
- Settings with validation and saved/default values.
- Safe-mode banner, disabled actions, focus visuals, and toast notifications.

Inspect hierarchy, text contrast, focus visibility, clipping, overflow, alignment, state clarity, and action priority. Build/test success alone is not sufficient evidence for visual completion.

## 17. Acceptance criteria

The redesign is complete only when:

1. Every existing Desktop view listed in this design uses the new design system.
2. Light, dark, and system themes remain functional and visually coherent.
3. The application is usable without clipping at 960 x 640 and polished at 1280 x 800.
4. Configure, Review, Execute, Local Ready, Completed, and Publish Pending are visibly distinct and preserve their existing logic.
5. Empty, validation, disabled, loading/busy, stale, failure, success, and recovery states are deliberately presented wherever the backing model exposes them.
6. Keyboard, screen-reader, focus, and automation behavior is preserved or improved.
7. No new UI control implies unsupported behavior.
8. No Desktop security or architecture boundary is weakened.
9. Automated verification is green and visual QA evidence covers the required screens, themes, and sizes.
10. No unrelated user changes, including the separate M10 design work in the same worktree, are modified or committed.
