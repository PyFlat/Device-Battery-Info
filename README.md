# Device Battery Info

A [Macro Deck 3](https://macro-deck.app/) plugin that surfaces battery state from several places at
once, on your deck and as button/flow variables:

| Source                          | How it reads                        | Percent | Charging | Time to full    |
| -------------------------------- | ------------------------------------ | ------- | -------- | ---------------- |
| This PC / laptop                | Win32 `GetSystemPowerStatus`        | yes     | yes      | later milestone |
| Android phone                   | `adb shell dumpsys battery`         | yes     | yes      | no              |
| Windows Bluetooth audio device  | PnP battery property via PowerShell | yes     | rarely   | no              |
| Other devices (a growing catalog) | device-specific (e.g. HID)        | varies  | varies   | varies          |

The first three rows are generic backends - they work with whatever hardware of that kind you have.
**Other devices** is different: it is a catalog of specific products someone has implemented and
tested against real hardware, one at a time, and it only ever lists exactly what is confirmed to work
today - never a whole brand or category. See what's currently in it from inside Macro Deck (the config
flow's "Other devices" step lists every entry) or in the `Models` list of each family under `Sources/`; this README intentionally
doesn't duplicate that list; it would go stale. **Adding a device you own is the main way this catalog
grows**, see [Contributing a device](#contributing-a-device) below.

## What it looks like on the deck

Two custom widget types - a multi-device **Battery panel** and a single-device **Battery tile** -
each with a config form to pick which sources it shows and what to display (level bar, percentage,
charging indicator, time to full, battery trend, low-battery threshold). Widgets update live between
polls, and both are registered as `[UiPreview]` scenarios, so Macro Deck's Developer Tools list and
render them in states that are awkward to reach live (low battery, charging, nothing configured).

Each configured device also becomes a set of Macro Deck variables
(`battery_<device>_percent`, `_status`, `_charging`, `_online`, `_time_to_full`, `_trend`,
`_trend_rate`) for button faces and flows, the plugin emits `charging-started` / `charging-stopped` /
`battery-low` events, and a "Refresh battery levels" action forces an immediate poll.

`_trend` reports how much a device's charge has moved recently as a signed percentage over whatever
window it actually covers, for example `-13%/1h` while discharging or `+28%/30m` while charging - the
window is not normalized to a fixed unit, since it takes at least a couple of minutes of history
before a device has one at all. `_trend_rate` is the same window normalized to percent per hour, for
automations that want a number to threshold against rather than the display text. Both reset a few
minutes after every plugin restart, since the history behind them is kept in memory only.

## Setting up devices

Devices are managed **inside Macro Deck** through the plugin's config flow: add one entry per device.
The first step picks a category - This PC, an Android phone over adb, a Windows Bluetooth device, or
**Other devices** - and Other devices then lets you pick a brand and a model from the catalog, with a
link to [`docs/adding-a-device.md`](docs/adding-a-device.md) for anything not listed. A catalog product
needs nothing more than the pick - the plugin already knows how to reach it - while This PC, adb and
Bluetooth ask for the one thing they need (an address, a device name), from a list where it can be
discovered. Editing an existing entry pre-fills its fields. There is no default device: a fresh install
shows nothing until you add one, and the widgets say so until then. A device's id - the
`battery_<id>_*` variable prefix - is the slug of its name, so renaming a device changes those
variable names.

Some catalog devices need more than one physical unit to be told apart (two identical mice, say); how
that is handled is backend-specific and documented where that backend lives, not here - see
[`docs/adding-a-device.md`](docs/adding-a-device.md) and [AGENTS.md](AGENTS.md) for the details behind
any one entry.

## Installing

Grab the packed `.macroDeckPlugin` artifact from the
[latest release](https://github.com/PyFlat-JR/Device-Battery-Info/releases) (or the store listing,
once published) and install it from Macro Deck's plugin manager. Windows only for now - see the
[compatibility table](#supported-platforms) below.

## Contributing a device

This plugin is built so that supporting a new device is a one-line or one-file change. A new model in
an existing family (say another Razer mouse) is one line in that family's `Models` list; a new protocol
is one class. [`docs/adding-a-device.md`](docs/adding-a-device.md) shows both. Please read
[CONTRIBUTING.md](CONTRIBUTING.md) first, in particular the note on AI-assisted contributions - AI
tools are welcome, but a device backend that was not actually exercised against the real hardware, or
a PR its author cannot explain, will not be merged.

## Requirements

- .NET SDK 10.0
- A running Macro Deck desktop app for [interactive debugging](#run-and-debug-against-macro-deck)

## Quick start

```bash
dotnet build
```

```bash
dotnet test
```

Build and tests need no Macro Deck installation. For an interactive session, use the checked-in
**Macro Deck - Real Host** launch profile after the one-time setup below.

## Project layout

```
src/DeviceBatteryInfo/
  Program.cs               builder chain: bind options, register registry + sources + poll loop
  manifest.json             identity, icon, win-x64 entrypoint
  BatteryIntegration.cs     IPluginIntegration + IVariableProvider + IEventProvider + IConfigFlowProvider
  BatteryIntegration.Widgets.cs   the same partial class: IWidgetTypeProvider + IUiProvider
  ConfigFlow/               the device config flow (add/edit steps, discovery, the "Other devices" catalog)
  Core/                     IBatterySource, BatteryReading, BatteryRegistry, BatteryPollingService,
                            BatteryPluginOptions, DeviceCatalog
  Sources/                  one folder per backend (SystemBattery, Razer, Adb, Bluetooth): a pure
                            parser, an IO wrapper behind an interface, and an IBatterySource(+Provider).
                            Hid/ is the shared HID transport, base class and family, Razer/ is one protocol file whose device list is one
                            line per supported mouse - see "Contributing a device" above
  Variables/                slot x field -> VariableDefinition, and the reverse resolve
  Ui/                       widget rendering, config view and preview scenarios
  Actions/                  the "Refresh battery levels" action
  Localization/Strings.resx default-culture strings; Strings.<culture>.resx per language
tests/DeviceBatteryInfo.Tests/
  BatteryIntegrationTests.cs      builds, initializes, the variables catalogue + a read work
  BatterySourceParsingTests.cs    dumpsys / Razer report / PnP / Win32 power-status parsers
  BatteryRegistryTests.cs         registry update / stale / retain, and catalog id round-trips
  ...and one file per other capability under test (widgets, config flow, catalog notifications)
```

[AGENTS.md](AGENTS.md) is the full, detailed rule set this plugin is written against (lifecycle,
async/concurrency, localization, logging, comment style, verification steps) - read it before making a
non-trivial change, human or AI.

## Supported platforms

This plugin currently ships **win-x64** only (`manifest.json` `entrypoints`), because several of its
sources are Windows-specific (Win32 power status, PnP/Bluetooth via PowerShell). The Razer HID path and
the `adb`-based phone source have no Windows dependency, so a macOS/Linux build is plausible future
work; see [`docs/adding-a-device.md`](docs/adding-a-device.md) if you want to help port a source.

## Building against a local SDK build

The plugin tracks the Macro Deck SDK's *published* packages and floats to the newest one, so a plain
`dotnet build` always resolves the latest release. While a change is still unreleased, pack the SDK
from a Macro Deck 3 checkout into this repository's `local-feed/` and build against that version:

```bash
dotnet pack MacroDeck.slnx -c Release -p:Version=3.0.0-local.1 -o <path-to-this-repo>/local-feed
```

```bash
dotnet build -p:MacroDeckSdkVersion=3.0.0-local.1
```

`NuGet.config` already lists `local-feed/` as a package source, and `MacroDeckSdkVersion` sets the
version for every Macro Deck package at once (see `Directory.Packages.props`). Nothing in the
repository pins the local version, so a plain `dotnet build` goes back to the published one.

Pick a version that cannot collide with a real release - `3.0.0-local.N` rather than reusing a
published preview version, which would put a hand-built package into the global NuGet cache under the
name of a published one.

## Localization

Every user-facing string is a key in `Localization/Strings.resx`, reached through the generated
`Strings` class - never a literal. Add a `<data>` entry, build, and use the generated `Strings.*`
member; see [AGENTS.md](AGENTS.md#localization) for the full rules (placeholders, plurals, adding a
language). The
[localization guide](https://docs.macro-deck.app/sdk/localization/) is the upstream reference.

`.resx` was chosen because [JetBrains Rider's Localization
Manager](https://www.jetbrains.com/help/rider/Localizing_Applications.html) reads it - every key as a
row, every culture as a column, missing translations highlighted - but nothing requires Rider; these
are plain `.resx` files, and any translator can work from a CSV export.

## Run and debug against Macro Deck

The project contains exactly one interactive launch profile: **Macro Deck - Real Host**. It launches
the plugin project directly, so Rider and Visual Studio attach the debugger to plugin code without a
wrapper or child-process attach. The profile connects in self-registering mode to the installed Macro
Deck desktop app at `http://127.0.0.1:8193`.

For the first run:

1. Start Macro Deck.
2. Open **Developer Tools → Plugin tokens**, create a token and copy it. It is shown only once.
3. Store the token in the source project's **.NET User Secrets** using one of the methods below. The
   project is already initialized; do not run `dotnet user-secrets init`.
4. Select **Macro Deck - Real Host** and start it with **Debug**.
5. Once enrollment succeeds, remove the token from User Secrets.

### Set the token in Rider or Visual Studio

In Rider, right-click `DeviceBatteryInfo` in the Solution Explorer and select
**Tools → .NET User Secrets**. In Visual Studio, right-click the same source project and select
**Manage User Secrets**. Do not select the `.Tests` project.

The IDE opens a `secrets.json` file stored in your user profile, outside this repository. Replace its
contents with:

```json
{
  "MacroDeck:Plugin:EnrollmentToken": "<paste the one-time token here>"
}
```

Save the file, then start **Macro Deck - Real Host**. After enrollment, reopen `secrets.json` and
remove the `MacroDeck:Plugin:EnrollmentToken` entry.

### Set the token from a terminal

From the repository root on macOS or Linux, use the following form. It reads the token without echoing
it and does not put the value in shell history or process arguments:

```bash
project="src/DeviceBatteryInfo/DeviceBatteryInfo.csproj"
printf "Enrollment token: "
read -rs md_enrollment_token
printf '\n'
printf '{"MacroDeck:Plugin:EnrollmentToken":"%s"}\n' "$md_enrollment_token" |
  dotnet user-secrets set --project "$project"
unset md_enrollment_token
```

After the first successful profile launch, remove the one-time token:

```bash
dotnet user-secrets remove "MacroDeck:Plugin:EnrollmentToken" --project "$project"
```

With PowerShell 7, use the equivalent masked-input form:

```powershell
$project = "src/DeviceBatteryInfo/DeviceBatteryInfo.csproj"
$token = Read-Host "Enrollment token" -MaskInput
@{ "MacroDeck:Plugin:EnrollmentToken" = $token } |
  ConvertTo-Json -Compress |
  dotnet user-secrets set --project $project
Remove-Variable token
```

Then remove it after enrollment:

```powershell
dotnet user-secrets remove "MacroDeck:Plugin:EnrollmentToken" --project $project
```

The profile persists the exchanged plugin credential under
`src/DeviceBatteryInfo/.macrodeck-dev-state/`, which is ignored by Git and excluded from the
packed artifact. Later profile launches reuse that credential.

User Secrets are local-only but not encrypted. Never put the enrollment token in `launchSettings.json`,
a shared IDE configuration, a literal command argument or a commit. If you intentionally clear the
local state, create a fresh token and repeat the User Secrets step. Self-registration only works
against a host on the same machine. See the official
[Rider User Secrets guide](https://www.jetbrains.com/help/rider/Manage_NET_user_secrets.html) and
[.NET Secret Manager guide](https://learn.microsoft.com/aspnet/core/security/app-secrets?view=aspnetcore-10.0)
for more background.

## The developer CLI

`macrodeck-plugin` validates, inspects, packs and conformance-tests the plugin. Interactive starts use
the launch profile above.

```bash
dotnet tool install --global MacroDeck.Plugin.Cli --prerelease
```

`--prerelease` is required while the 3.0 SDK is in preview: only preview versions are published, and
`dotnet tool install` picks stable ones by default. Drop it once 3.0 ships.

The tool needs the **ASP.NET Core shared framework**, not just the .NET runtime - its stub host is a
real Kestrel server.

| Command                    | What it does                                                                                                                                             |
| --------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `build`                    | Publishes every runtime identifier the manifest declares using `macrodeck-build.json`, then packs the result.                                            |
| `validate`                 | Checks a manifest, version directory or artifact against the real manifest reader, the JSON Schema, the permission vocabulary and declared file digests. |
| `inspect`                  | Reports what installing an artifact would find - entrypoints, permissions, dependencies, conflicts, compatibility, signature shape, size.                |
| `pack`                     | Builds a `.macroDeckPlugin` artifact, validating the manifest first and recomputing `files[]` digests.                                                   |
| `run`                      | Launches the plugin against a real host or a disposable stub one, streaming its output.                                                                  |
| `test`                     | Runs the conformance suite and writes a text, JSON or Markdown report.                                                                                   |
| `sign`, `verify`, `keygen` | Creator signing for a packed artifact.                                                                                                                    |

### Running without a host

```bash
macrodeck-plugin run --project src/DeviceBatteryInfo --stub-host
```

`--stub-host` starts a disposable in-process host, so this needs no Macro Deck installation: the plugin
registers, negotiates the protocol and initializes, and its log output is streamed until you interrupt
it. `--artifact <file>` does the same for a packed artifact, which is what proves an entrypoint path in
the manifest matches what `build` actually wrote. Drop `--stub-host` to attach to the running desktop
app instead; for debugging with breakpoints, use the launch profile above rather than this.

### Packing a release

`build` is the whole path: it reads `macrodeck-build.json`, publishes the declared platform into its
`runtimes/<rid>/` slot and packs the artifact.

```bash
macrodeck-plugin build --source src/DeviceBatteryInfo --output ./artifacts
```

```bash
macrodeck-plugin inspect --artifact ./artifacts/<id>-<version>.macroDeckPlugin
```

Packing validates before it writes, so a bad manifest never becomes an artifact. It discards whatever
`files[]` the source manifest declared and recomputes every digest from disk, and fills in `languages`
from `Localization/`. It cannot sign anything: sign *after* packing, against the packed manifest, or the
digest will not match.

A plain `dotnet build -c Release` does not produce a packable layout - the manifest points at
`runtimes/<rid>/`, which only `build` assembles. Use `validate` against a built artifact or a version
directory rather than against `bin/Release/net10.0`.

### Conformance

```bash
macrodeck-plugin test --project src/DeviceBatteryInfo --report markdown --output conformance.md
```

The suite drives a real session against the plugin: capability contracts, invocation and cancellation
semantics, reconnect and resume behaviour, the reserved `/_macrodeck/*` endpoints, and logging limits.
Checks are Required or Recommended, each with a stable id (`MDC0401`, …) you can select with `--check`
or `--category`. A check can report `SKIP` with a reason when the plugin gives it nothing to observe.

Exit codes make it usable as a CI gate - `0` conformant, `1` the plugin is wrong, `2` usage error, `3`
input unreadable, `4` cancelled. `1` and `3` are deliberately distinct: a missing file is an
environment problem, not a verdict about the plugin.

## Testing

```bash
dotnet test
```

The test project references `MacroDeck.Plugin.Testing`, which provides a loopback test host, fakes and
assertions for testing a plugin without a running Macro Deck. `PluginTestHarness.Create` builds the
plugin from the same `Action<PluginHostBuilder>` `Program.cs` uses - no socket, no host, no built
executable - with a `ManualTimeProvider` for the clock and a `FakeIntegrationContext` you can seed and
assert against:

```csharp
await using var harness = PluginTestHarness.Create(builder => builder.RegisterIntegration<BatteryIntegration>());
await harness.InitializeIntegrationsAsync();
```

Drive capabilities through the typed clients it exposes (`harness.Actions`, `harness.Variables`, …)
rather than calling an executor directly, so parameter binding is under test too.

The conformance suite above covers the protocol contract; these tests are for the plugin's own
behaviour.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for the workflow, the coding rules ([AGENTS.md](AGENTS.md)),
and the project's policy on AI-assisted contributions. Please also read the
[Code of Conduct](CODE_OF_CONDUCT.md).

## License

MIT - see [LICENSE](LICENSE). Macro Deck itself is licensed under Apache 2.0.

## Further reading

- [Adding a device](docs/adding-a-device.md) - the contract a new battery source implements
- [Plugin development docs](https://github.com/Macro-Deck-App/Macro-Deck-3/tree/main/docs/plugin-development)
- [Sample plugins](https://github.com/Macro-Deck-App/Macro-Deck-Sample-Plugins) - a worked example per capability
- [`plugin-hosting.md`](https://github.com/Macro-Deck-App/Macro-Deck-3/blob/main/docs/plugin-development/plugin-hosting.md) - the builder API, registration modes, the artifact format and every `MACRO_DECK_PLUGIN_*` variable
- [`sdk-reference.md`](https://github.com/Macro-Deck-App/Macro-Deck-3/blob/main/docs/plugin-development/sdk-reference.md) - every interface and record the plugin builds against
- [`cli.md`](https://github.com/Macro-Deck-App/Macro-Deck-3/blob/main/docs/plugin-development/cli.md) - every CLI command and option
- [`testing-plugins.md`](https://github.com/Macro-Deck-App/Macro-Deck-3/blob/main/docs/plugin-development/testing-plugins.md) - the test harness, the fakes and the manual clock
- [`conformance.md`](https://github.com/Macro-Deck-App/Macro-Deck-3/blob/main/docs/plugin-development/conformance.md) - the conformance suite and its check ids
- [`analyzers.md`](https://github.com/Macro-Deck-App/Macro-Deck-3/blob/main/docs/plugin-development/analyzers.md) - the compile-time diagnostics
