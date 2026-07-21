# Logic Pro integration

## Supported platform

Logic project creation is available only in the manually started desktop
workflow on macOS. The generic DAW contracts and the Logic infrastructure
assembly compile on every supported platform, but the capability check disables
the Logic GUI settings on Windows and Linux. If a context nevertheless requests
Logic on another platform, the optional workflow step reports that it was
skipped instead of failing the extraction workflow.

The integration does not install Logic Pro and does not run a daemon, agent,
watcher, or background service. It looks for `Logic Pro.app` in the standard
macOS Applications locations when the GUI starts.

## Template setup

Create and save a normal Logic Pro project with the desired sample rate, track
defaults, routing, plug-ins, and other project settings. Select that `.logicx`
project as the template in WingSessionExtractor. The integration treats the
selection as a filesystem template: it copies the complete project package and
never opens or modifies the original.

Choose a separate Logic project output directory. Each run plans this layout:

```text
<Logic output>/YYYY-MM-DD_WingSession_<session-id>/
  YYYY-MM-DD_WingSession_<session-id>.logicx
```

The date defaults to the selected input directory's last-write date and the
identifier defaults to its final directory name. Invalid filename characters
are replaced. If the target directory or project already exists, the step
returns a conflict and does not overwrite it.

## Accessibility permission

Logic does not expose all required audio-import operations through a supported
scripting API. WingSessionExtractor therefore uses macOS Accessibility UI
automation for those operations. Grant permission at:

**System Settings → Privacy & Security → Accessibility → WingSessionExtractor**

The adapter checks Accessibility before opening the project. A denied check or
authorization error produces a message containing the settings path above. It
does not attempt to change the setting or bypass macOS security.

Depending on the macOS version, the first run may also display Apple Events
consent prompts for controlling Logic Pro or System Events. Accept those prompts
for the integration to continue.

## Track ordering

Only extracted files named `CH<number>.wav` are accepted. Channel numbers are
parsed using invariant numeric rules and imported in numeric order, so `CH2`
precedes `CH10` regardless of locale or input order. Duplicate channel numbers
are rejected, including differently padded or cased names such as `CH01.wav`
and `ch001.WAV`.

Gaps are reported as warnings by default. A caller that supplies an explicit
expected channel count turns missing channels into a validation failure.

## Automation and timeouts

The adapter copies the template and opens the copy with `/usr/bin/open`. A
single dedicated AppleScript resource activates Logic and isolates all System
Events UI scripting. Processes are started directly with individual argument
list entries; WingSessionExtractor does not invoke `/bin/sh` or interpolate user
paths into shell commands.

Cancellation-aware, bounded polling covers:

- Logic process startup;
- project window opening;
- import-dialog availability;
- completion of each track import; and
- project save completion.

Each timeout identifies the operation that did not complete. Cancelling stops
polling and child automation processes where technically possible and maps to a
cancelled workflow result. Logic may finish an already accepted UI operation
before stopping; cancellation cannot make Logic's own UI actions transactional.
If UI automation has already begun, the copied project is retained for
inspection and its path is included in a failure message. An incomplete copy is
removed only when failure or cancellation occurs before Logic is started.

An internal `DawProjectRequest.DryRun` option validates the configuration,
orders the tracks, calculates the intended project path, and returns warnings
without copying the template or starting Logic. The main GUI intentionally does
not expose this diagnostic switch yet.

## Known UI automation limitations

Accessibility automation depends on Logic's menu names and accessibility tree.
The current driver expects the English **File → Import → Audio File…** command.
Logic or macOS updates, another UI language, modal dialogs, plug-in alerts, or a
changed workspace can prevent a probe from succeeding. These cases end in a
bounded, named timeout rather than an unbounded wait.

Keep Logic visible and avoid interacting with its menus or file dialog during
the import. The initial implementation imports tracks one at a time and stops
after saving the project; it does not automate bouncing.

## Troubleshooting

- **Logic settings are disabled:** run the GUI on macOS and confirm Logic Pro is
  installed in `/Applications` or `/System/Applications`.
- **Accessibility error:** enable WingSessionExtractor in **System Settings →
  Privacy & Security → Accessibility**, then restart the application.
- **Import dialog timeout:** use Logic's English UI, close modal dialogs, and
  verify that **File → Import → Audio File…** is available in the template.
- **Project-open timeout:** open the copied `.logicx` package manually to check
  template compatibility and any Logic version-upgrade prompt.
- **Conflict:** move or remove the existing deterministic project directory;
  WingSessionExtractor never overwrites it automatically.
- **Track validation failure:** confirm files use unique `CH<number>.wav` names
  and still exist in the extraction output directory.
