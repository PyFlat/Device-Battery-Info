# Device Battery Info

A [Macro Deck 3](https://macro-deck.app/) plugin that shows the battery level of your PC, your
Android phone, your Bluetooth audio devices and a growing list of specific gaming peripherals, right
on your deck.

## Contents

- [Features](#features)
- [Supported devices](#supported-devices)
- [Installing](#installing)
- [Setting up devices](#setting-up-devices)
- [Contributing](#contributing)
- [Development](#development)
- [License](#license)
- [Further reading](#further-reading)

## Features

**Two deck widgets**, each with a config form to choose which devices it shows and what it displays
(level bar, percentage, charging indicator, time to full, battery trend, low-battery threshold):

- **Battery panel** shows several devices at once.
- **Battery tile** shows a single device. Pressing a widget refreshes the levels immediately.

Widgets update live between polls. The battery trend is shown as a signed change over the window it
covers, for example `-13%/1h` while discharging or `+28%/30m` while charging. It needs a couple of
minutes of history and starts over whenever the plugin restarts.

**Events** you can use in flows: `charging-started`, `charging-stopped` and `battery-low`.

**A "Refresh battery levels" action** that forces an immediate poll.

## Supported devices

### Generic sources

These work with whatever hardware of that kind you have.

| Source                         | How it reads                        | Percent | Charging |
| ------------------------------ | ----------------------------------- | ------- | -------- |
| This PC / laptop               | Win32 `GetSystemPowerStatus`        | yes     | yes      |
| Android phone                  | Macro Deck's own adb connection     | yes     | yes      |
| Windows Bluetooth audio device | PnP battery property via PowerShell | yes     | rarely   |

### Specific devices ("Other devices")

A catalog of individual products that someone has implemented and tested against real hardware. It
only lists exactly what is confirmed to work, never a whole brand or category, so a model that is not
listed here is not supported even if it shares a brand or protocol with one that is.

| Brand    | Model                | Type    | Connection        | Percent | Charging | Notes                                                                                                        |
| -------- | -------------------- | ------- | ----------------- | ------- | -------- | ------------------------------------------------------------------------------------------------------------ |
| Razer    | DeathAdder V3 Pro    | Mouse   | Dongle or cable   | yes     | yes      |                                                                                                              |
| Razer    | Basilisk V3 Pro      | Mouse   | Dongle or cable   | yes     | yes      |                                                                                                              |
| Logitech | G Pro X Superlight 2 | Mouse   | Receiver or cable | yes     | yes      |                                                                                                              |
| Logitech | G Pro X Wireless     | Headset | Dongle            | approx. | yes      | Reports a voltage, so the percentage is an estimate. It reads nothing while the headset is off.              |

The exact list is always the "Other devices" step of the config flow, which is built from the same
code. If this table and the flow ever disagree, the flow is right; please fix the table.

Own a device that is not listed? Adding it is the main way this catalog grows, see
[Contributing](#contributing).

## Installing

Download the packed `.macroDeckPlugin` file from the
[latest release](https://github.com/PyFlat-JR/Device-Battery-Info/releases) (or from the store
listing, once published) and install it from Macro Deck's plugin manager.

The plugin currently ships for **Windows x64** only, because several sources are Windows-specific
(Win32 power status, PnP/Bluetooth via PowerShell). The HID path used by the mice and headsets and the
`adb` phone source have no Windows dependency, so a macOS/Linux build is plausible future work. See
[Adding a device](docs/adding-a-device.md) if you want to help port a source.

## Setting up devices

Devices are managed inside Macro Deck through the plugin's config flow. Add one entry per device:

1. Pick a category: **This PC**, **Android phone** (over adb), **Windows Bluetooth device** or
   **Other devices**.
2. Fill in the one thing that category needs, from a list where it can be discovered: a device name
   for Bluetooth, a phone for adb (each list shows the device's current battery level). For
   **Other devices**, pick a brand and a model from the catalog; nothing more is needed, the plugin already knows how to reach it.
3. Name the device.

An Android phone is read through Macro Deck's own adb connection, so there is no adb to install or
point the plugin at. Turn on **Settings > ADB** and **Allow plugins to use ADB** in Macro Deck; until
then the phone list stays empty and the phone reads as unavailable. The list shows every phone Macro
Deck's adb sees; one marked as needing authorization needs the USB debugging prompt accepted on the
phone. A phone that is not attached yet can be entered by hand: a USB phone by its serial, a wireless
one by `host:port`, which the plugin connects to on its own (Android 11+ needs the phone paired first).

Editing an existing entry pre-fills its fields. There is no default device: a fresh install shows
nothing until you add one, and the widgets say so until then.

Some catalog devices need more than one physical unit to be told apart (two identical mice, say). How
that is handled depends on the device type and is documented in
[Adding a device](docs/adding-a-device.md) and [AGENTS.md](AGENTS.md).

## Contributing

Supporting a new device is a one-line or one-file change. A new model in an existing family (say
another Razer mouse) is one line in that family's `Models` list; a new protocol is one class.
[Adding a device](docs/adding-a-device.md) shows both.

Please read [CONTRIBUTING.md](CONTRIBUTING.md) first, in particular the note on AI-assisted
contributions: AI tools are welcome, but a device backend that was not actually exercised against the
real hardware, or a PR its author cannot explain, will not be merged. Also see the
[Code of Conduct](CODE_OF_CONDUCT.md).

[AGENTS.md](AGENTS.md) is the full rule set the plugin is written against (lifecycle,
async/concurrency, localization, logging, comment style, verification steps). Read it before making a
non-trivial change, human or AI.

## Development

Requirements: the .NET SDK 10.0, and a running Macro Deck desktop app only if you want to
[debug interactively](#run-and-debug-against-macro-deck).

```bash
dotnet build
dotnet test
```

Build and tests need no Macro Deck installation.

### Project layout

```
src/DeviceBatteryInfo/
  Program.cs               builder chain: bind options, register registry + sources + poll loop
  manifest.json            identity, icon, win-x64 entrypoint
  BatteryIntegration.cs    IPluginIntegration + IEventProvider + IConfigFlowProvider
  BatteryIntegration.Widgets.cs   the same partial class: IWidgetTypeProvider + IUiProvider
  ConfigFlow/              the device config flow (add/edit steps, discovery, the "Other devices" catalog)
  Core/                    IBatterySource, BatteryReading, BatteryRegistry, BatteryPollingService,
                           BatteryPluginOptions, DeviceCatalog
  Sources/                 one folder per backend (SystemBattery, Razer, Logitech, Adb, Bluetooth): a pure
                           parser, an IO wrapper behind an interface, and an IBatterySource(+Provider).
                           Hid/ is the shared HID transport, base class and family; a brand adds one
                           protocol file whose device list is one line per supported model
  Variables/               slot x field -> VariableDefinition, and the reverse resolve
  Ui/                      widget rendering, config view and preview scenarios
  Actions/                 the "Refresh battery levels" action
  Localization/Strings.resx   default-culture strings; Strings.<culture>.resx per language
tests/DeviceBatteryInfo.Tests/
  one file per capability under test (integration, source parsers, registry, widgets, config flow,
  catalog notifications, HID family and protocols)
```

### Run and debug against Macro Deck

The project has exactly one interactive launch profile, **Macro Deck - Real Host**. It launches the
plugin project directly, so Rider and Visual Studio attach the debugger to plugin code without a
wrapper or child-process attach. The profile connects in self-registering mode to the installed Macro
Deck desktop app at `http://127.0.0.1:8193`.

First run:

1. Start Macro Deck.
2. Open **Developer Tools → Plugin tokens**, create a token and copy it. It is shown only once.
3. Store the token in the source project's **.NET User Secrets** (see below). The project is already
   initialized; do not run `dotnet user-secrets init`.
4. Select **Macro Deck - Real Host** and start it with **Debug**.
5. Once enrollment succeeds, remove the token from User Secrets.

The profile persists the exchanged plugin credential under `src/DeviceBatteryInfo/.macrodeck-dev-state/`,
which is ignored by Git and excluded from the packed artifact. Later launches reuse that credential.

<details>
<summary>Setting the token in Rider or Visual Studio</summary>

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

</details>

<details>
<summary>Setting the token from a terminal</summary>

On macOS or Linux, from the repository root, this reads the token without echoing it and keeps the
value out of shell history and process arguments:

```bash
project="src/DeviceBatteryInfo/DeviceBatteryInfo.csproj"
printf "Enrollment token: "
read -rs md_enrollment_token
printf '\n'
printf '{"MacroDeck:Plugin:EnrollmentToken":"%s"}\n' "$md_enrollment_token" |
  dotnet user-secrets set --project "$project"
unset md_enrollment_token
```

After the first successful launch, remove the one-time token:

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

</details>

User Secrets are local-only but not encrypted. Never put the enrollment token in
`launchSettings.json`, a shared IDE configuration, a literal command argument or a commit. If you
intentionally clear the local state, create a fresh token and repeat the User Secrets step.
Self-registration only works against a host on the same machine. See the official
[Rider User Secrets guide](https://www.jetbrains.com/help/rider/Manage_NET_user_secrets.html) and
[.NET Secret Manager guide](https://learn.microsoft.com/aspnet/core/security/app-secrets?view=aspnetcore-10.0)
for more background.

### The developer CLI

`macrodeck-plugin` validates, inspects, packs and conformance-tests the plugin. Interactive starts use
the launch profile above.

```bash
dotnet tool install --global MacroDeck.Plugin.Cli --prerelease
```

`--prerelease` is required while the 3.0 SDK is in preview: only preview versions are published, and
`dotnet tool install` picks stable ones by default. Drop it once 3.0 ships. The tool needs the
**ASP.NET Core shared framework**, not just the .NET runtime, because its stub host is a real Kestrel
server.

| Command                    | What it does                                                                                                                                             |
| -------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `build`                    | Publishes every runtime identifier the manifest declares using `macrodeck-build.json`, then packs the result.                                            |
| `validate`                 | Checks a manifest, version directory or artifact against the real manifest reader, the JSON Schema, the permission vocabulary and declared file digests. |
| `inspect`                  | Reports what installing an artifact would find: entrypoints, permissions, dependencies, conflicts, compatibility, signature shape, size.                 |
| `pack`                     | Builds a `.macroDeckPlugin` artifact, validating the manifest first and recomputing `files[]` digests.                                                   |
| `run`                      | Launches the plugin against a real host or a disposable stub one, streaming its output.                                                                  |
| `test`                     | Runs the conformance suite and writes a text, JSON or Markdown report.                                                                                   |
| `sign`, `verify`, `keygen` | Creator signing for a packed artifact.                                                                                                                   |

**Running without a host.** `--stub-host` starts a disposable in-process host, so no Macro Deck
installation is needed. The plugin registers, negotiates the protocol and initializes, and its log
output is streamed until you interrupt it. `--artifact <file>` does the same for a packed artifact,
which proves that an entrypoint path in the manifest matches what `build` actually wrote. Drop
`--stub-host` to attach to the running desktop app instead. For debugging with breakpoints, use the
launch profile above.

```bash
macrodeck-plugin run --project src/DeviceBatteryInfo --stub-host
```

**Packing a release.** `build` is the whole path: it reads `macrodeck-build.json`, publishes the
declared platform into its `runtimes/<rid>/` slot and packs the artifact.

```bash
macrodeck-plugin build --source src/DeviceBatteryInfo --output ./artifacts
macrodeck-plugin inspect --artifact ./artifacts/<id>-<version>.macroDeckPlugin
```

Packing validates before it writes, so a bad manifest never becomes an artifact. It discards whatever
`files[]` the source manifest declared, recomputes every digest from disk and fills in `languages`
from `Localization/`. It cannot sign anything: sign *after* packing, against the packed manifest, or
the digest will not match. A plain `dotnet build -c Release` does not produce a packable layout, so
use `validate` against a built artifact or a version directory rather than against
`bin/Release/net10.0`.

**Conformance.** The suite drives a real session against the plugin: capability contracts, invocation
and cancellation semantics, reconnect and resume behaviour, the reserved `/_macrodeck/*` endpoints and
logging limits. Checks are Required or Recommended, each with a stable id (`MDC0401`, ...) you can
select with `--check` or `--category`, and a check can report `SKIP` with a reason when the plugin
gives it nothing to observe.

```bash
macrodeck-plugin test --project src/DeviceBatteryInfo --report markdown --output conformance.md
```

Exit codes make it usable as a CI gate: `0` conformant, `1` the plugin is wrong, `2` usage error, `3`
input unreadable, `4` cancelled. `1` and `3` are deliberately distinct, since a missing file is an
environment problem, not a verdict about the plugin.

### Testing

`dotnet test` runs the unit tests. The test project references `MacroDeck.Plugin.Testing`, which
provides a loopback test host, fakes and assertions for testing a plugin without a running Macro Deck.
`PluginTestHarness.Create` builds the plugin from the same `Action<PluginHostBuilder>` `Program.cs`
uses, with a `ManualTimeProvider` for the clock and a `FakeIntegrationContext` you can seed and assert
against:

```csharp
await using var harness = PluginTestHarness.Create(builder => builder.RegisterIntegration<BatteryIntegration>());
await harness.InitializeIntegrationsAsync();
```

Drive capabilities through the typed clients it exposes (`harness.Actions`, `harness.Variables`, ...)
rather than calling an executor directly, so parameter binding is under test too. The conformance
suite covers the protocol contract; these tests are for the plugin's own behaviour.

### Building against a local SDK build

The plugin tracks the Macro Deck SDK's *published* packages and floats to the newest one, so a plain
`dotnet build` always resolves the latest release. While a change is still unreleased, pack the SDK
from a Macro Deck 3 checkout into this repository's `local-feed/` and build against that version:

```bash
dotnet pack MacroDeck.slnx -c Release -p:Version=3.0.0-local.1 -o <path-to-this-repo>/local-feed
dotnet build -p:MacroDeckSdkVersion=3.0.0-local.1
```

`NuGet.config` already lists `local-feed/` as a package source, and `MacroDeckSdkVersion` sets the
version for every Macro Deck package at once (see `Directory.Packages.props`). Nothing in the
repository pins the local version, so a plain `dotnet build` goes back to the published one. Pick a
version that cannot collide with a real release, `3.0.0-local.N` rather than a published preview
version, which would put a hand-built package into the global NuGet cache under the name of a
published one.

### Localization

Every user-facing string is a key in `Localization/Strings.resx`, reached through the generated
`Strings` class, never a literal. Add a `<data>` entry, build, and use the generated `Strings.*`
member. See [AGENTS.md](AGENTS.md#localization) for the full rules (placeholders, plurals, adding a
language) and the upstream
[localization guide](https://docs.macro-deck.app/sdk/localization/).

`.resx` was chosen because [JetBrains Rider's Localization
Manager](https://www.jetbrains.com/help/rider/Localizing_Applications.html) reads it, with every key
as a row and every culture as a column, but nothing requires Rider. These are plain `.resx` files, and
any translator can work from a CSV export.

## License

MIT, see [LICENSE](LICENSE). Macro Deck itself is licensed under Apache 2.0.

## Further reading

- [Adding a device](docs/adding-a-device.md): the contract a new battery source implements
- [Plugin development docs](https://github.com/Macro-Deck-App/Macro-Deck-3/tree/main/docs/plugin-development)
- [Sample plugins](https://github.com/Macro-Deck-App/Macro-Deck-Sample-Plugins): a worked example per capability
- [`plugin-hosting.md`](https://github.com/Macro-Deck-App/Macro-Deck-3/blob/main/docs/plugin-development/plugin-hosting.md): the builder API, registration modes, the artifact format and every `MACRO_DECK_PLUGIN_*` variable
- [`sdk-reference.md`](https://github.com/Macro-Deck-App/Macro-Deck-3/blob/main/docs/plugin-development/sdk-reference.md): every interface and record the plugin builds against
- [`cli.md`](https://github.com/Macro-Deck-App/Macro-Deck-3/blob/main/docs/plugin-development/cli.md): every CLI command and option
- [`testing-plugins.md`](https://github.com/Macro-Deck-App/Macro-Deck-3/blob/main/docs/plugin-development/testing-plugins.md): the test harness, the fakes and the manual clock
- [`conformance.md`](https://github.com/Macro-Deck-App/Macro-Deck-3/blob/main/docs/plugin-development/conformance.md): the conformance suite and its check ids
- [`analyzers.md`](https://github.com/Macro-Deck-App/Macro-Deck-3/blob/main/docs/plugin-development/analyzers.md): the compile-time diagnostics
