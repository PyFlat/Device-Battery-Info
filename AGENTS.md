# Agent guidance

This file must be kept up to date. When a rule here stops matching reality, or a new rule emerges from
work in this repository, update this file as part of that change rather than leaving it to drift.

This folder is **Device Battery Info** (`manifest.json` `name`), a Macro Deck 3 out-of-process plugin,
scaffolded from `macrodeck-plugin new`. It reads battery state from three generic backends (the host
computer (Windows, macOS or Linux), an Android phone over adb, Bluetooth devices on all three) plus a
growing catalog of specific products under "Other devices" (each model lives in a device family, see
`Sources/DeviceFamily.cs`), and exposes each as a set of Macro Deck variables plus charging/low events,
alongside a custom deck widget (a multi-device panel and a single-device tile).

[README.md](README.md) is the human-facing guide: how to build, how to run against a real host, how to
pack. This file is the rule set for writing the plugin. Read it before changing code. The template
package itself is covered by
[packaging/README.md](https://github.com/Macro-Deck-App/Macro-Deck-Plugin-Template/blob/main/packaging/README.md).

## Orientation

```
src/DeviceBatteryInfo/
  Program.cs               builder chain: bind options, register registry + sources + poll loop
  manifest.json            identity, icon, win-x64, osx-arm64 and linux-x64 entrypoints (one managed
                           build)
  macrodeck-build.json     the publish target per entrypoint
  BatteryIntegration.cs    IPluginIntegration + IVariableProvider (on-demand catalog, push) +
                           IEventProvider + IConfigFlowProvider (AllowsMultipleConfigurations)
  BatteryIntegration.Widgets.cs   the same partial class: IWidgetTypeProvider + IUiProvider
  BatteryIntegration.Issues.cs    the same partial class: IIntegrationIssueProvider (the Linux HID
                           permission issue)
  ConfigFlow/              DeviceConfigFlow (host-rendered steps per device: basics picks a name and
                           a category, "Other devices" adds a brand/model step; a backend that still
                           needs an address or a device name (adb, Bluetooth) then gets a details
                           step, a catalog model completes straight from the model step; async, enumerates
                           Bluetooth and Android phones + pre-fills on edit), DeviceModelCatalog (brand -> model, collected
                           from the registered device families, for the "Other devices" step),
                           DeviceEntryReader (entries -> BatterySlot[]),
                           DeviceConfigKeys, SystemDeviceDiscovery
  Core/DeviceAccessProblems.cs   device ids whose hardware is connected but refused to open (HidFamily
                           records, the issue reads)
  Core/DeviceCatalog.cs    the live device set, replaced from the config entries
  Core/IDeviceDiscovery.cs public: lists present Bluetooth devices and attached Android phones for the
                           config-flow pickers (HID
                           enumeration is kept for a future "scan for supported devices" step)
  Ui/                      widget rendering: BatteryWidgetView (deck tree), DeviceGlyphs (one vector
                           icon per BatterySourceKind, plus the charging bolt), BatteryWidgetConfigView
                           (config form), UiViewSession (UiView -> IUiSession adapter),
                           BatteryWidgetModel, BatteryWidgetTypes (descriptors + JSON Schema),
                           BatteryWidgetSamples (fixed demo models shared by the widget "sample"
                           surface and the previews), BatteryWidgetPreviews ([UiPreview] scenarios
                           the host's Developer Tools list and render), TextWidth (estimated text
                           widths for the list percentage)
  Actions/RefreshBatteryAction.cs   "refresh" action: wakes the poll loop
  Core/                    IBatterySource, BatteryReading, BatteryRegistry, BatteryPollingService,
                           BatteryPluginOptions, BatterySlots (config -> device set, one place),
                           BatteryTrendTracker (per-device charge history -> BatteryTrend),
                           BatteryTrendFormatter (BatteryTrend -> display text / a normalized rate),
                           ChargingInference (a rising level -> Charging, for sources that cannot tell)
  Sources/                 one folder per backend (SystemBattery, Adb, Bluetooth, and the HID brands
                           Razer, Logitech, Corsair, Rapoo, Aula, Sony), each a pure parser + an IO
                           wrapper behind an interface + IBatterySource(+Provider);
                           SystemBattery has ISystemPowerReader (Windows: kernel32, macOS: pmset,
                           Linux: /sys/class/power_supply) and Bluetooth has IBluetoothBatteryReader
                           (Windows: cfgmgr32, macOS: system_profiler plus pmset accps, Linux:
                           busctl against BlueZ; all three share BluetoothSnapshot), picked by
                           OperatingSystem in BatterySourceRegistration;
                           ExternalProcess runs the command line tools;
                           BatterySourceRegistration wires them into DI. DeviceFamily.cs holds the
                           device-family contracts (IDeviceFamily, DeviceModel, SimpleDeviceFamily,
                           DeviceFamilyProvider); every family in the assembly is registered by
                           scanning. Hid/ has the shared HID transport/interop, HidProtocol
                           (what a brand implements) and HidFamily; Razer/RazerProtocol.cs lists the
                           supported mice
  Variables/BatteryVariableCatalog.cs   slot x field -> VariableDefinition, and the reverse resolve
  Localization/Strings.resx   default-culture strings; Strings.<culture>.resx per language
  Assets/icon.svg
  Properties/launchSettings.json   the single real-host debug profile
tests/DeviceBatteryInfo.Tests/
  BatteryIntegrationTests.cs      builds, initializes, the variables catalogue + a read work
  BatterySourceParsingTests.cs    Razer report / PnP / Win32 power-status parsers, the Android picker
  BatteryTrendTrackerTests.cs     trend windows, segments per charging state, the trend text
  BatteryWidgetViewTests.cs       every widget tree built through a real UiView, the config form, glyph
                                  paths, responsive variants, the previews
  CatalogNotificationTests.cs     the integration never takes a catalog notifier, and re-initializes cleanly
  DeviceConfigFlowTests.cs        the config flow end to end, the way the host drives it
  DeviceEntryReaderTests.cs       config entries -> device slots, including the legacy keys
  DeviceFamilyTests.cs            model ids, the catalog, DeviceFamilyProvider
  HidFamilyTests.cs               probing, path caching, identical units, refused (unopenable) devices
  HidProtocolTests.cs             a second protocol on the shared HID plumbing, dongle or cable, push-only
  <Brand>ProtocolTests.cs         per-brand HID frames captured on real hardware (Logitech, Corsair,
                                  Rapoo, AULA, Sony)
  MacOsSourceTests.cs             pmset and system_profiler parsers, the Bluetooth snapshot, the system
                                  source
  LinuxSourceTests.cs             power_supply and BlueZ parsers, sysfs HID interface and unit keys
  HidTransportPlatformTests.cs    macOS and Windows interface and unit keys, the bounded exchange on a fake
                                  channel, vendor-collection matching
  BatteryRegistryTests.cs         registry update / stale / retain, and catalog id round-trips
  HardwareTests.cs                [Explicit, Category=Hardware]: lists Bluetooth and HID interfaces, reads
                                  every supported HID device through the plugin, and reads one again while a
                                  foreign poller hammers the same control interface (the Synapse case); all
                                  output goes through HardwareReport so every line has the same shape
docs/linux-setup.md               end-user guide the Linux issue opens; carries the udev rule inline
packaging/linux/70-device-battery-info.rules   udev rule granting the seat user the hidraw nodes of the
                                  supported models only; one line per product id (LinuxUdevRuleTests
                                  checks it, and that the guide's copy is identical)
```

Design knowledge that is not obvious from the code alone:

- **The whole variable set is an on-demand catalog, not an eager list.** `BatteryIntegration.Variables`
  returns an empty array on purpose: declaring a variable both in that eager list and as `OnDemand`
  (`SupportsCatalog => true`) is a contract violation the host rejects outright. Localization and
  widget-type registration happen in the same registration pass, so that rejection takes every provided
  variable down with it too (`"Registered 0 provided variable(s)"` / `AlreadyExists`) - keep `Variables`
  empty and let the host reach each `battery_<id>_<field>` id through `DiscoverAsync`/`ResolveAsync`
  instead. `SupportsPush` + `OnAttachedAsync` still push live updates to an attached session; `ReadAsync`
  answers a direct `get`.
- **`DeviceCatalog` is the single source of device identity at runtime.** Providers, the variable
  catalogue and the widgets all read `DeviceCatalog.Devices`. It starts empty and is populated only by
  `BatteryIntegration.InitializeAsync` from the config-flow entries (`DeviceEntryReader`) - there is no
  default device and no options-based seeding, so a fresh install shows nothing until the user adds a
  device through the config flow (the widgets' empty state points them there). A device `Id` (the
  `battery_<id>_*` variable prefix, a public API) is `slug(name)`, deduped in entry-id order.
- **Every host callback can be refused while the poll is busy.** The host throttles all of a
  plugin's callbacks together, adb included, and a refused `RegisterWidgetTypeAsync` marks the whole
  integration unusable (every widget shows "unavailable"). Wrap config reads and registrations in
  `HostCallRetry.RunAsync`, which retries "too quickly" and connection refusals with backoff.
- **`BatteryIntegration` is a singleton** (the hosting DI registers integrations that way) and also a
  capability-handler type, so the whole class obeys the no-blocking rule: variable push is
  fire-and-forget `async Task`, never awaited from the `registry.Changed` handler.
- The poll loop is a separate `BackgroundService`; the integration never starts work in its ctor.
- **Bump `manifest.json` `version` on every artifact you hand over for testing.** Re-installing the
  same version leaves Macro Deck on its cached per-plugin state (localization catalog, widget-type
  registration); a stale catalog renders every plugin string as `[[plugin:<id>:Key]]` and makes the
  widget types look "gone". A version bump forces a clean re-register.
- **Widgets:** there is no public `IUiSession` base - wrap a `MacroDeck.Ui.Runtime.UiView` yourself
  (`Ui/UiViewSession.cs`, copied from the hosting package's internal `UiPreviewSession`). Reactivity
  is `UiState<T>` + `UiValue.From(() => state.Value...)` closures; pushing a new value re-renders.
  Every `UiElement.Key` must match `^[A-Za-z0-9][A-Za-z0-9._-]*$` - a `/` throws at session open,
  which conformance does not catch, so the `BatteryWidgetViewTests` build each tree through a real
  `UiView`. A widget `config` surface is served by this plugin's own `IUiProvider.CreateSessionAsync`
  (kind `"config"`, `entryPoint == "widget-config"`), not by the hosting config-flow path.
  `BatteryWidgetConfigView` follows the host's own Action Button form: `UiTabs` (Devices, and Appearance
  with the display switches under a "Show" heading) and `Segmented` choices whose `UiOption.Icon` names a `UiIcons` value for short icon choices.
  The properties pane beside the actions editor is narrow (about 280 px): a switch in a row wraps its
  label and stacks it above the switch, and one whose row partner is hidden jumps to the right, so every
  switch gets its own line and only small segmented controls share a `UiConfigStack` row
  (`Wrap = false`, `RowWeight = 1` each). Two segmented controls in one row must both be icon-only or
  both text: the host pads a strip of icon-only options differently, so a mixed pair never lines up. Keep labels and option names short enough not to truncate at that width. Tabs and rows
  only change rendering: the values stay in the session's `UiState`, and a field's node id stays its bare
  key, which is what a `VisibleWhen` resolves against (only an object or array input starts a scope).
  `BatteryWidgetViewTests` checks every condition names a field in the built tree.
  **Every `UiLength` is a fraction of the view basis, not a pixel** - a bar needs both `MainSize`
  (its box) and `Thickness` (its track), texts beside a `Fill` sibling need a `MainSize`, and
  `Padding` is the corner-radius safe area (`BatteryWidgetView.SafeArea`, radius from the
  `cornerRadius` surface attribute). Plugins ship no images: device icons are `UiShape` paths in
  `DeviceGlyphs`, drawn in the unit square the renderer scales to the shape's box (so give the box a
  square `UiFrame`), absolute `M L H V C Q A Z` only, filled nonzero - a solid part clockwise, a
  cut-out counter-clockwise, and a cut-out never where two solid parts overlap (the winding sums to
  1 there and it disappears). `BatteryWidgetViewTests` checks every path against the renderer's
  grammar. A shape's `Color` takes a hex only, not a theme role, so glyphs and rings carry the state
  colour (`BatteryWidgetRow.Color` for the widget's `colors` scheme, all Apple system colours so every
  scheme has the same saturation; every scheme shows a level at or below the threshold in red even
  while charging and a stale or unknown one in grey) and the percentage uses the primary text role. A ring is a full-turn `UiGauge`
  inside a `UiModifier` with `Frame.AspectRatio = 1` and a `UiLayer` for the gauge, the bolt and the
  face; the gauge is inset by half the bolt's height minus half its stroke, which puts a charging
  bolt exactly in the gap the gauge leaves at the top (`StartAngle`/`EndAngle`, 0 is up, clockwise).
  The face (glyph over percentage) must fit the gauge's inner circle, radius about 0.35 of the
  diameter: the corners of the percentage line are what collide, so check them, not just the height.
  The ring fills its slot and the diameter only sizes its parts, so `ringSize` (50-100 %, default
  100) shrinks a ring by capping its frame (`MaxWidth`/`MaxHeight`) at the scaled diameter and sizing
  the parts from that; at 100 % the frame stays uncapped, so existing widgets render as before.
  `namePosition: "inside"` puts the name in the face under the percentage (the panel only with
  `showNames`): the glyph shrinks (`FaceGlyphNamed`), and the name sits in a `UiModifier` whose
  `MaxWidth` keeps its corners within the inner circle's chord at the bottom of the face, with a
  `MinSize` so a long name shrinks before it truncates. The line below the ring goes, and the stacked
  tile and `Arrange` give its room back to the ring. The wide tile keeps the name in its details.
  Every `UiLength` is a fraction of the whole widget's basis, never of a grid cell, so the ring panel
  estimates its ring diameter (`BatteryWidgetView.Arrange`: the column count that gives the largest
  ring for the device count and aspect) and sizes each ring's parts reactively from that; a
  `UiResponsive` picks the aspect bucket. The view builder rejects `Fill`/`MainSize` on a responsive
  variant's root and on a modifier's child, and `BatteryWidgetViewTests` builds every layout through
  a real `UiView` to catch that. The list layout sizes the way the host's own Weather widget does:
  small type, `UiSize.FromBasis(fraction)` with a `maxOfCross` only as a safety rail; each row hugs
  its content and the `Fill` body centres the rows. Next to a `Fill` sibling a text's measured width
  is underestimated, so the list percentage has a fixed `MainSize`, sized to its own text so a
  charging bolt sits beside the number; that width is estimated with `TextWidth` (four character
  classes fitted to SF Pro Semibold, erring wide). The plugin never sees pixels or fonts, so wherever a
  layout depends on whether text fits, let the reader measure with `UiFirstFit` (Macro Deck PR 1139)
  instead of estimating.
  The host's facts behind that, read from its renderer: the basis is `min(width, height)` of the
  widget, a `UiResponsive` matches variants against the box its parent gives it (`MinAspect`, or
  `MinWidth`/`MinHeight` in 120 px cells; min inclusive, max exclusive) and takes the **first** match,
  so variants must not overlap (`Responsive_variants_never_overlap` checks every built tree), and a
  text with `MinSize` shrinks toward it to fit its box
  before it truncates. Texts in one row have no shrink priority, so a caption beside the name
  truncates both when it does not fit: the list body is a `UiFirstFit` with the rows inline (caption
  beside the name) first and stacked last. A shrunk text counts as fitting, so the inline layout's
  name and caption have no `MinSize`. It switches the whole list, not each row, because an unsized
  first-fit takes its last layout's size and every row would be as tall as a stacked one. A progress bar's `StartColor`
  and `EndColor` are always the same hex - the renderer always paints a `linear-gradient`.
- **A short press refreshes until the user sets their own Short Press action.** Both widget types set
  `SupportsFlows` (Macro Deck PR 960), so the host runs the actions the user bound to the widget, like a
  built-in one, and `DefaultShortPressAction` (PR 1111, host and SDK beta.15) names this plugin's own
  `refresh` action (`RefreshBatteryAction.ActionId`). The host runs that default only while the
  widget's Short Press flow is missing, empty or fully disabled, so the user's action always wins and
  a Long Press flow works beside the default refresh. The tree must declare no `press` event: one owns
  the gesture (`treeClaimsGesture`), and the host then skips both the flows and the default. A host
  older than beta.15 ignores the default, so a press there does nothing. `SupportsFlows` alone shows no action editor:
  the host runs the flows under the widget data's top-level `flows` key, so `BatteryWidgetConfigView`
  serves a `UiActionsListEditor` bound to `flows` in the `UiWidgetConfiguration.Editor` region (seeded
  from the stored data, and allowed by the `DataSchema`, whose `additionalProperties: false` would
  otherwise reject it). Without an `Editor` region the desktop draws the properties as one full-width
  pane, which looks stretched.
- **Macro Deck draws the widget border, not the plugin.** The host draws the ring around any widget
  from the stored `border` key (`{ "style", "color" }`), so the config form only adds
  `UiWidgetAppearance.Section(data, UiWidgetAppearanceFields.Border)` and the `DataSchema` must allow
  `border`. The section's colour field is conditioned on `style` inside the `border` object scope
  (`border.style`). Background and text colour come from the same section (`BackgroundColor`,
  `TransparentBackground`, `LabelColor`) and are drawn by the plugin: `UiWidgetAppearance.Read` fills
  `BatteryWidgetOptions.BackgroundColor`/`TextColor`, the root stack's `Background` takes the stored
  value as is (`transparent` on the root drops the deck's tile face), and both descriptors declare
  them in `AppearanceProperties` so the Set Background Color / Set Label Color actions reach the
  widgets. A text `Color` takes `#rrggbb` only (an 8-digit hex is ignored), so secondary and muted
  text are mixed toward an opaque background (80 % and 60 %) and use the full colour over the theme's
  face or a transparent one. `UiGauge` has no track colour, so the ring's empty track stays the
  theme's on a custom background (accepted).
- **Widget previews:** `Ui/BatteryWidgetPreviews.cs` has one `static` parameterless method per
  scenario, each `[UiPreview(name, View = nameof(BatteryWidgetView), Profile = UiPreviewProfiles.Widget)]`
  returning a `UiElement`. `UiPreviewCatalog.Scan` (run by the hosting `ui` capability over the
  plugin assembly) discovers them with no registration wiring and no `developer-preview` surface
  declaration; the host's Developer Tools list and render them. Keep them building the real
  `BatteryWidgetView` from `BatteryWidgetSamples` models, and keep `scan.Diagnostics` empty
  (`BatteryWidgetViewTests` asserts both).
- **`BatteryTrendTracker` reports a delta over whatever window it actually has, never an
  extrapolation to a fixed unit.** It is a plain DI singleton (not a capability handler) that
  subscribes to `BatteryRegistry.Changed` once in its constructor and lives for the process, so it
  needs no `InitializeAsync`/`ShutdownAsync` wiring. History is per device and segmented by charging
  state - a charge-state flip starts a fresh segment so a discharge rate and a charge rate never mix
  and only appends a sample when the percent actually moves, since polling is far more frequent
  than the charge level changes. `GetTrend` withholds a reading until the oldest sample in the
  current segment is at least `MinWindow` (2 minutes) old, so a fresh segment does not publish a
  reading dominated by poll jitter. `BatteryTrendFormatter.FormatText` turns that into text like
  `-13%/1h` or `+28%/30m` (the window is rounded for readability, not normalized to "per hour"),
  and `PercentPerHour` into a signed rate for automations. History is in-memory only and is lost on
  every plugin restart or update by design - it self-heals within `MinWindow`, which is simpler than
  persisting it under `MACRO_DECK_PLUGIN_DATA_DIRECTORY`. The widget caption falls back to the trend
  text when there is no time-to-full to show (most sources never report one). The rings layout has no
  caption; its own `showRingTrend` flag (default off) adds a muted trend line under each ring, below
  the name, and `Arrange` reserves room for it. It is a separate key rather than `showTrend` because
  released widgets already store `showTrend: true`, which would shrink every existing ring. Both the `trend`
  and `trend-rate` variable suffixes are public API like every other field suffix in
  `BatteryVariableCatalog`.
- **Charging is inferred where a source cannot tell.** Bluetooth only ever reports a level, so
  `BluetoothBatterySource` fills in `Discharging` with `BatteryReading.StatusIsAssumed`; an `Unknown`
  status counts the same. `ChargingInference` (a DI singleton like `BatteryTrendTracker`, applied by
  the poll loop before `BatteryRegistry.Update`) turns such a reading into `Charging` once the level is
  `MinRise` (2) points above its lowest in the last `RiseWindow` (15 min), and back on any drop below
  the peak or after `IdleTimeout` (30 min) without a rise, restarting its history so the old low cannot
  re-trigger. It sits before the registry on purpose: the widgets, the `charging`/`status` variables,
  the trend segments and the charging events all see the same state (the user chose this over a
  widget-only guess). A state a source reported is never replaced.
- **Bluetooth battery data lives on a different PnP node than the one the user picks, and is only
  cached opportunistically.** `WindowsBluetoothBatteryReader` reads `DEVPKEY_Bluetooth_Battery` from
  Windows' own PnP device tree. Every physical device shows up as many PnP nodes sharing one 6-byte
  Bluetooth address (`BTHENUM\DEV_<mac>\...` is the single root node whose `FriendlyName` matches what
  Windows Settings/Device Manager show; everything else is a sibling SDP/profile node, e.g. the
  `{0000111E-...}` Hands-Free Audio Gateway node, named `"<device> Hands-Free AG"`). The battery
  property is never set on the root node - only on whichever sibling node Windows' Bluetooth stack has
  actually queried it through (commonly HFP), and plenty of present, working devices never get one at
  all, because nothing ever triggered that query. So `ListDevicesAsync` (the config-flow picker) must
  never filter on "has a cached value right now" - only on whether the device could ever get one at
  all: a sibling node advertising SDP class `0000111E` (Hands-Free) is that capability signal, confirmed
  against real hardware (a pure A2DP speaker with no microphone never has that node and never gets a
  reading; a device that has the node but no value yet still belongs in the list). List every present
  root node (`^(BTHENUM|BTHLE)\DEV_`) that clears that bar, by name, and let `ReadRawAsync` (and its
  `Unavailable` fallback, already handled) deal with "no value yet". A read cannot resolve by
  `FriendlyName` alone either: the level belongs to whichever node shares the name's Bluetooth address.
  Extracting that address is not a bare 12-hex-digit regex - every classic SDP node's GUID ends in the
  constant Bluetooth Base UUID (`...-8000-00805F9B34FB`), itself 12 hex digits, and matches
  indiscriminately across every other paired device's nodes too if not excluded. The real address only
  ever appears immediately after `DEV_` or `&0&` (the radio-address separator every child node's
  `InstanceId` has), so the extraction regex must anchor on one of those two prefixes.
  **The tree is read through cfgmgr32 (`NativeDeviceProperties`), never PowerShell.** It lists the
  present nodes of the `BTHENUM`, `BTHHFENUM`, `BTHLE` and `BTHLEDEVICE` enumerators
  (`CM_Get_Device_ID_ListW`, enumerator + present filter) and reads each node's name (FriendlyName,
  else DeviceDesc, as `Get-PnpDevice` does) and level (`CM_Get_DevNode_PropertyW`); that takes tens of
  milliseconds for every device. The PowerShell it replaced cost 3.3 s per device and per poll, six
  devices at once took 13.5 s against the 10 s read timeout (so from about five Bluetooth devices every
  one went stale), `Get-PnpDeviceProperty` costs about 0.3 s per node, and batched over many nodes it
  returned values for only some of them, a different set on every run. `BluetoothPnpLevels` is the pure
  part: `ByName` maps every named node (a manual name may be a sibling's, such as the Hands-Free AG) to
  its device's level, BLE nodes first, names compared ignoring case as `Get-PnpDevice -FriendlyName` did;
  `Pickable` is the picker rule above. Reads go through `BluetoothSnapshot` like macOS, so every
  Bluetooth source of a poll shares one walk. `HardwareTests.Reads_every_bluetooth_device_in_one_poll`
  reads them all on real hardware and fails past 5 s.
- **macOS sources are command line tools behind the same interfaces, run through `ExternalProcess`.** Use
  absolute paths (`/usr/bin/pmset`, `/usr/sbin/system_profiler`): the plugin inherits its environment
  from the Macro Deck host. `PmsetBatteryParser` reads the first `InternalBattery` line only (no line or
  `present: false` means no battery, as on a desktop Mac). `BatteryStatus` has no not-charging value, so
  `AC attached; not charging` (Optimized Battery Charging) maps to Full at 100 % and Unknown below, never
  to Charging. `SystemBatterySourceProvider` remembers that a battery was seen so a poll does not run
  pmset twice; until then its probe has a 5 second timeout and a failure or hang counts as no battery yet
  (logged once), so it cannot stall discovery for the other providers; a battery that later reports
  `present: false` makes the source throw every poll (a stale value) instead of disappearing.
  `SystemProfilerBluetoothParser` reads `device_connected` only, because `device_not_connected` entries
  keep a stale cached level; a device reports its main battery, else the lower of Left and Right, and the
  case is ignored. `MacBluetoothBatteryReader` merges `system_profiler` with `pmset -g accps`
  (accessories such as a Logitech MX mouse have a battery only in the latter; it is matched by name into
  devices system_profiler lists as connected, so a not-connected accessory or an AirPods case entry never
  becomes a device, and a failing accps call costs only those levels), and serves reads from a 5 second
  snapshot behind a semaphore so several Bluetooth sources of one poll run one process; a failed or
  cancelled fetch is never cached and each waiter fetches for itself, and the config-flow picker never
  uses the snapshot. Keep the parsers pure and tested with real captured output; the shape of a connected
  entry with a battery was only ever seen in fixtures marked as such until a real one is captured.
- **The Android phone goes through Macro Deck's adb, never a spawned `adb`.** `AdbBatterySource` uses
  the host's `IAndroidDeviceManager` (DI-provided, gated by the manifest's `host:adb` permission) and
  its `GetBatteryStateAsync`, so there is no process runner, no `dumpsys` parser and no executable
  setting any more; an old entry's `adbExecutable` key is ignored. `Access` other than `Available`
  means the user has not enabled ADB for plugins, and the source throws so the poll loop logs it once
  and keeps the last value. The address is a serial or `host:port`; only the latter is passed to
  `ConnectAsync`, because a USB serial cannot be connected to. The host allows 4 concurrent adb calls
  per plugin and refuses the fifth with `RateLimited`, and adb counts toward the plugin's shared
  callback throttle, so `AdbBatterySourceProvider` gates every phone's reads through one semaphore of
  2 (a user with 4 phones made the poll refuse its own widget-type registration). The config flow's phone picker (`SystemDeviceDiscovery.ListAndroidDevicesAsync`) reads each
  attached phone's battery one at a time for the same reason, and shares `DeviceConfigFlow.PickerField`
  with the Bluetooth picker: a list when something is attached, a text field when nothing is or an
  existing entry is being edited, plus a manual override field. Tests use `FakeAndroidDeviceManager`
  from `MacroDeck.Plugin.Testing`.
- **A device is a model in a device family, and a family is one file.** `IDeviceFamily` (in
  `Sources/DeviceFamily.cs`) is a protocol plus its `DeviceModel`s. A new model in an existing family is
  one line in that family's `Devices`/`Models`; a new protocol is one class: a `HidProtocol` for a USB
  HID brand, a `SimpleDeviceFamily` (list the models, implement `ReadAsync`) for anything else, or a
  raw `IDeviceFamily` when it must locate hardware itself. Keep these contracts non-generic and free of
  constructor plumbing: a contributor should not need to understand the framework to add a protocol.
  Families and protocols are registered by assembly scan, `DeviceModelCatalog` is built from them, and `DeviceFamilyProvider` hands each family the
  configured slots (`DeviceType.Catalog`, `CatalogDeviceId`) whose model it owns. A model id is
  `slug(brand + name)`, persisted in user data, so never rename a shipped model. A config entry stores
  `type=catalog` plus `catalogDevice=<model id>`; the reader still accepts the older
  `type=razer-deathadder-v3-pro` (model id as the type) and ignores the retired `vendorId`/`productId`
  keys. Brand and model names are proper nouns and are the one deliberate exception to the
  no-user-facing-literal rule. Do not add a `DeviceType` value, config key or config-flow branch for a
  family; that is exactly what this design removed. Adding or removing a model also means updating the
  "Supported devices" table in `README.md` and, for a USB HID model, the Linux udev rule and its copy in
  `docs/linux-setup.md`. See `docs/adding-a-device.md`.
- **HID feature reports are shared plumbing plus a base family; Razer is the first protocol on it.**
  `Sources/Hid/` holds `NativeHid` (raw `hid.dll` feature-report interop), `IHidTransport`/
  `HidSharpTransport` (enumeration plus a protocol-agnostic `ExchangeAsync` that retries until the
  caller's `isComplete` accepts the response) and `HidFamily`, the one family that runs every
  `HidProtocol`. It owns everything not specific to a protocol: finding candidates by USB id, probing
  which collection answers (by running the protocol's own `ReadAsync`), caching the answering path, and
  binding several entries to distinct physical units. A protocol supplies its brand, USB vendor id,
  devices and `ReadAsync` only. Razer uses feature reports (`HidReportKind.Feature`); Logitech HID++
  writes output reports and reads input reports on the vendor interface `FF00:0002`
  (`HidReportKind.InputOutput`, `IHidTransport.ExchangeReportsAsync`), where the receiver also pushes
  unrelated notifications, so a reply must match device index, feature index and function. Behind a
  Logitech receiver the mouse has no product id of its own, the receiver's is listed, and only device
  index 1 answered on the tested setup. Logitech's headsets are a second protocol
  on the same HID++ framing: `LogitechHidppProtocol` owns the feature lookup, the call and the reply
  matching, and a product line supplies only its interface, device index, battery feature id, function
  and the parsing of the answer (`LogitechProtocol` for the mice, `LogitechHeadsetProtocol` for the
  headsets). Verified on a G Pro X Wireless `0ABA`: same HID++, but on `FF43:0202`,
  addressed as the receiver (device index `0xFF`), and they have no battery feature at all - only
  `0x1F20` (ADC measurement), which answers a voltage in mV, not a percentage, plus a state byte where
  `0x03` means charging. The mV are mapped through HeadsetControl's G Pro calibration points, linearly
  interpolated; that curve is an estimate, so treat the percentage as approximate and never as a value
  to calibrate other code against. A powered-off headset reads far below the curve, which is why
  `ParseVoltage` throws there instead of publishing 0%. Ask the device for its feature table
  (`0x0001` getCount + getFeatureId) before assuming a feature index. `Sources/Razer/RazerProtocol.cs` is the Razer report layout, command ids
  and checksum. **Only the DeathAdder V3 Pro has been tested on real hardware.** The command class
  (0x07, "power") and ids are plausibly shared across Razer mice, but a mouse is added to
  `RazerProtocol.Devices` only after its battery was read on the device.
  The dongle answers on `mi_00`, confirmed against real hardware. HidSharp's `Open` only ever requests
  `GENERIC_READ|GENERIC_WRITE` and throws `DeviceIOException` when the control interface declines;
  `NativeHid` does what hidapi (and so the old Dart app) does - `CreateFile` with read+write,
  then retry with **zero access**, which still carries the `HidD_SetFeature` / `HidD_GetFeature`
  IOCTLs. On Windows HidSharp is kept only for enumeration + feature-report length. The response byte is
  `resp[10]` of the **raw** hidapi buffer (byte 0 is the report id) - never a span that skips the id
  byte. The dongle periodically answers a poll with a not-yet-ready placeholder frame (status
  `resp[1]` not `0x02`, command echo `resp[7..8]` absent, payload zeroed) while it is still talking to
  the mouse; `resp[10]` there is `0`, so trusting it without checking for a completed frame first would
  surface as a spurious 0% reading every few minutes. `RazerProtocol.IsCompletedResponse` gates on
  status + command echo, and `HidSharpTransport.ExchangeAsync` re-issues the exchange until the predicate
  holds or `HidChannel.ReadBudget` (2 s) is up, then throws so the poll loop keeps the last good value instead of
  publishing the 0. **The retry timing is tuned against a second process on the same control interface,
  not just against the dongle.** Razer Synapse polls the very same interface, and `HidD_GetFeature`
  returns whatever the device answered *last*, so Synapse's reply routinely lands in our buffer - with the
  mouse on the cable that made every read fail, while wireless happened to win the race often enough. Two
  things fix it: the window between `SetFeature` and `GetFeature` starts at 3 ms and only doubles up to
  50 ms across attempts (`SettleDelay(attempt)`) - a wide first window is what hands the race to the other
  poller, and a slow dongle still gets its time on a later attempt - and the delay between attempts is
  randomized (20-120 ms), because a fixed rhythm can stay in lockstep with the other poller and never win.
  `HardwareTests.Reads_while_a_foreign_poller_hammers_the_same_interface` reproduces exactly that: it runs
  a competing serial-number poll at 20 ms on every Razer feature interface and still expects a reading.
  A Razer wireless mouse has one product id per link, and only the live one answers the power class: on the
  Basilisk V3 Pro the cable is `0x00AA` and the dongle `0x00AB`, and a dongle whose mouse is off the radio
  answers every command - power *and* firmware - with status `0x04` ("command timeout"), which is exactly
  how "the mouse is not on this link" looks. Never read that as a wrong transaction id: `0x1F`, `0x3F`,
  `0x08`, `0x88`, `0x00`, `0x1E`, `0x9F` and `0x80` were all measured against a wired Basilisk V3 Pro and
  every one of them answered `0x04` on the idle dongle and succeeded on the cable. `FindCandidates` orders by interface ascending; `HidFamily` probes
  each once with the shorter `HidChannel.ProbeBudget` (for both report kinds), so a silent interface
  cannot stall the poll, and caches the answering path. A wireless mouse has a dongle product id and a cable product id, so a
  `HidDeviceInfo` lists both (`FindCandidates` runs once per id) and `HidChannel.ProductId` says which is
  in use. When a read fails `HidFamily` forgets the remembered interface, so the next poll probes again
  and picks up a dongle-to-cable switch; without that, an unplugged-from-radio mouse whose dongle is
  still in the PC would fail forever on the dongle. Two entries for the same model (two identical mice) are
  handled: `HidFamily` groups the candidates by physical unit (`PhysicalUnitKey` - USB serial, else the
  parent-instance token in the device path) and deals each entry a distinct unit in entry-id / unit-key
  order. Units with no serial are only distinguishable by port, so a re-plug can swap which entry is
  which; the user renames to match. `HidFamilyTests` and `HidProtocolTests` cover the family and a second protocol;
  `HardwareTests` (`[Explicit]`, `Category=Hardware`) exercises the real devices. The
  config flow never asks for a USB id or an interface. There is no in-UI "custom device" path by
  design (a raw USB id alone cannot drive the Razer HID protocol); an unlisted device is a model in a
  family.
- **The Corsair VOID headsets answer one output report.** `Sources/Corsair/CorsairProtocol.cs`
  covers the VOID PRO Wireless headset: output report `C9 64` on the vendor interface
  `FFC5:0001` (`HidReportKind.InputOutput`), answered by input report `0x64` with the level in byte 2
  (bit `0x80` is set while the mic boom is raised/muted, verified by toggling it; masked off) and the
  state in byte 4 (1 connected, 2 low, 4 full, 5 charging, 0 headset off - `ParseBattery` throws there
  instead of publishing 0%). The layout comes from HeadsetControl and a Node tray app. Verified on a
  VOID PRO Wireless `0A75` (dongle, interface 3: `FFC5:0001` is in 5 / out 20): discharging and
  charging both read with a level, off answers state 0, and full on the cable answers state 4 with the
  level still at 96.
- **Rapoo and AULA were mapped on real hardware, not from a spec.** `Sources/Rapoo/RapooProtocol.cs`
  (VT3 PRO, dongle `1215`, cable `4415`) sends nothing: the mouse pushes `BB B0 51 E8 03 <state>
  <level>` about every 3.2 s on the `FF00:0002` collection with 7-byte reports (a sibling `FF00:0002`
  with 10-byte reports stays silent). State 1 is on battery, 2 charging; on current firmware a
  charging mouse sends level `0x7F`, which reads as Charging with no percent. There is no full state:
  a full mouse on the cable sends state 2 with level 100, read as Full. **On the cable the pushes are
  irregular** (3 s bursts, then 30-60 s of silence, while the mouse is in use), so a 4 s read and the
  probe often miss them: the probe then falls back to the silent dongle collection and the reading
  goes stale. Accepted as a known limitation; the fix would be a background listener caching the last
  report per path, instead of listening per poll. Over the dongle the push is a steady 3.2 s. It is the one protocol
  that listens only, so `ExchangeReportsAsync` skips the write for an empty request, and it overrides
  `HidProtocol.ReadBudget` (4 s), because the 400 ms probe and 2 s read budgets are shorter than the
  push interval. A switched-off mouse is silent, so the read times out. PID `1215` is not the VT3 PRO
  id that the open-source Rapoo tools document (`1231`, `14A5`), and their report layouts do not
  apply to it. `Sources/Aula/AulaProtocol.cs` (F75 on the Compx receiver `3554:FA09`) sends the
  battery-hub / womier-l65-linux frame on `FF02:0002`: report `0x13`, command `0x4A`, 20 bytes, and
  the last byte is the byte sum of the rest. The reply has the level in byte 5 and the state in byte 6
  (`0x01` on battery, `0x10` with the cable in). With the cable in the level always reads 100, even at
  a real 97%, so that reads as Charging with no percent (charging and full cannot be told apart). The receiver also pushes
  unsolicited `0x13 0A` frames, so a reply must match the command and the checksum. In wired mode the
  keyboard enumerates as a separate Sinowealth device (`258A:010C`), with an unknown protocol, and is
  not read. Command `0x44` returns a 10-frame configuration dump (probably lighting) that is not
  mapped. The 8BitDo Ultimate (`2DC8:3106`) is deliberately absent: its dongle presents an
  XInput *wired* pad (`WIRED`/`FULL`) and has no vendor collection, so no battery reaches the PC.
- **The DualSense needs one feature read over Bluetooth.** `Sources/Sony/SonyProtocol.cs` (`054C:0CE6`,
  gamepad collection `0001:0005`) only listens, like Rapoo; the controller streams hundreds of
  reports a second, so the default budgets suffice. The status byte (layout from Linux
  hid-playstation) is byte 53 of the 64-byte USB report `0x01`, or byte 54 of the 78-byte Bluetooth
  report `0x31`: the low nibble is the level in tenths (shown as level x 10, which matches what
  the user sees elsewhere, not hid-playstation's x 10 + 5), the high nibble 0 discharging, 1
  charging, 2 full. A fresh Bluetooth link sends only a short `0x01` report with no battery;
  reading feature `0x05` (calibration) switches it to `0x31` until it disconnects. Verified on
  real hardware by reconnecting the controller: only `0x01` before, `0x31` right after the read.
  Nothing on this Windows (GameInput, Edge) did that switch on its own. That read goes through
  `IHidTransport.GetFeatureAsync`, which only reads; `ExchangeAsync` always writes a feature first,
  and writing `0x05` is something no reference implementation does. Lightbar, player LEDs, rumble
  and adaptive triggers live in output report `0x02` (USB) and are not used.
- **HID on macOS uses HidSharp for the feature reports and enumeration, through `IFeatureChannel`.**
  `HidSharpTransport` opens a device with `NativeHid` on Windows, `LinuxHidraw` on Linux and
  `HidStream.SetFeature/GetFeature` on macOS; the loop, timing and buffer layout are unchanged apart
  from those open/set/get call sites. HidSharp lists only devices with a real USB id on macOS (it returns
  nothing on a Mac with only built-in Apple devices), paths look like
  `.../IOUSBHostInterface@N/AppleUserUSBHostHIDDevice` with `N` in hex, and the feature report length
  includes the report id like on Windows. A refused `Open` blocks for about a second inside HidSharp, so
  off Windows the whole exchange (`ExchangeAsync` and `ExchangeReportsAsync`) runs in `Task.Run` bounded
  by its budget. Budget expiry must be an `InvalidOperationException`, never an
  `OperationCanceledException`: `HidFamily`, `DeviceFamilyProvider` and
  `BatteryPollingService.DiscoverAsync` let a cancellation escape, which aborts the poll for every
  source. The abandoned task ends on its own and disposes its channel; a probe costs at most the
  exchanges of the protocol's `ReadAsync` times `HidChannel.ProbeBudget`, and a device that stays refused
  pays that on every poll. A blank or all-zero serial (the Razer reports `000000000000`) does not
  identify a unit, so `PhysicalUnitKey` falls back to the Windows parent-instance token or the macOS path
  above `/IOUSBHostInterface@`. macOS also presents one device per interface with every top-level
  collection inside it (the G Pro X Wireless has consumer control, `FF43:0202` and `FF00:0001` in one),
  where Windows presents one device per collection, so `HidCandidate.Usages` lists all of them and
  `HidFamily` matches a vendor collection anywhere in the list; the HID++ write there must be as long as
  that report id (`OutputReportLength`), not the longest output report of the interface. Read on macOS:
  Razer Basilisk V3 Pro (cable and dongle) and the Logitech G Pro X Wireless headset; the Logitech mice
  are unverified there.
- **Linux reads files and D-Bus, and needs a udev rule for HID.** `PowerSupplyBatteryParser` reads
  `/sys/class/power_supply/*/uevent` (no process): `TYPE=Battery` only, never `SCOPE=Device` (a
  peripheral the kernel drives, such as a Logitech mouse through hid-logitech-hidpp) and never
  `PRESENT=0`. A battery reports either energy (`ENERGY_*` in uWh with `POWER_NOW` in uW) or charge
  (`CHARGE_*` in uAh with `CURRENT_NOW` in uA), the two are never summed together, and a rate can be
  negative on some drivers. Several batteries (ThinkPads) combine into one reading. `Not charging` (a charge
  threshold) maps like macOS's `AC attached`. `BlueZBatteryReader` runs
  `/usr/bin/busctl --system --json=short call org.bluez / org.freedesktop.DBus.ObjectManager GetManagedObjects`
  once per snapshot; `BlueZDeviceParser` lists connected `org.bluez.Device1` objects by `Alias` (the
  desktop's name, falling back to `Name`) with `org.bluez.Battery1.Percentage`. busctl wraps every variant
  as `{ "type", "data" }`. **Never use HidSharp's `SetFeature`/`GetFeature` on Linux**: against a
  DeathAdder V3 Pro dongle they returned an all-zero buffer and no error, while `HIDIOCSFEATURE` /
  `HIDIOCGFEATURE` on the same node answered correctly, so `LinuxFeatureChannel` issues those ioctls
  itself (`LinuxHidraw`, report id at byte 0 as everywhere). HidSharp is still used on Linux for
  enumeration, report lengths and the input/output report exchange (plain hidraw read/write, unverified
  on hardware there). HidSharp on Linux uses hidraw and its `DevicePath` is the sysfs path
  (`/sys/devices/.../1-5.3/1-5.3:1.1/0003:1532:00B7.0002/hidraw/hidraw1`): the interface number is the
  decimal `N` of the `<port>:<config>.N` node and `PhysicalUnitKey` is the USB device directory above it
  (`l:` prefix). Like macOS, one hidraw node carries every collection of its interface, so the non-Windows
  report-length and usage handling applies. hidraw nodes are `root:root 0600` by default and HidSharp
  must open a node even to read its report lengths, so without the udev rule every candidate comes back
  with all lengths 0 (`HidCandidate.CouldNotOpen`). `FindCandidates` returns those flagged rather than
  dropping them, and `HidFamily` records an entry in `DeviceAccessProblems` (logging once per episode)
  when its model is connected but no interface opens; a readable interface or an absent device clears it.
  On Linux `BatteryIntegration` turns that into an Error integration issue naming the entries (only those
  still in `DeviceCatalog`, so a deleted entry drops out). An issue's button runs only `ResolveIssueAsync`
  and the SDK's follow-ups are `None` or `StartConfigFlow`, so the plugin opens `docs/linux-setup.md`
  itself, as System-Media does for its VLC add-on, and a failure toast carries the URL. It runs
  `/usr/bin/xdg-open` with the URL as its one argument: never `UseShellExecute` (a PATH lookup in the
  host's environment, and the pattern a Store review already rejected once as PowerShell). The host re-lists issues on its own; the issue
  clears at the next poll once the device opens. Removing the udev rule does not revoke access until the
  device is replugged or `udevadm trigger` runs, so test the issue only after that.
  `GetIssuesAsync` is polled by the host and must stay a read of recorded state. The beta.15
  `PluginTestHarness` has no `Issues` member yet, so tests call the integration from the harness's DI.
  **The udev rule is a security boundary.** It tags `uaccess` (which must happen before
  `73-seat-late.rules`, hence the `70-` prefix) per supported USB product id, never per vendor: a vendor
  grant would let every program the user runs read that vendor's keyboards. A Bluetooth HID node (uhid)
  has no USB attributes and is matched by its HID device name instead (`KERNELS=="0005:054C:0CE6.*"`
  for the DualSense, unverified). The guide prints the rule inline, one single-quoted `printf` argument per line, into `sudo tee`
  (fish, the default on CachyOS, has no heredocs); never tell users to
  download it as root from a branch, because a udev rule can `RUN+=` any program as root. Read on Linux: the Razer DeathAdder V3 Pro (dongle and cable).

Authoritative upstream documentation is at <https://docs.macro-deck.app/> (the Macro Deck 3 repository itself is not public):
[plugin hosting](https://docs.macro-deck.app/reference/plugin-hosting/) (builder, registration modes, manifest, artifact,
environment variables), [capability parity](https://docs.macro-deck.app/reference/capability-parity/) (what behaves differently
out of process), [SDK packages](https://docs.macro-deck.app/reference/sdk-packages/), the [feature guides](https://docs.macro-deck.app/features/) (one
per capability, e.g. [integration issues](https://docs.macro-deck.app/features/integration-issues/)),
[analyzers](https://docs.macro-deck.app/reference/analyzers/), [conformance](https://docs.macro-deck.app/reference/conformance/),
[testing](https://docs.macro-deck.app/features/testing/) and the [CLI](https://docs.macro-deck.app/cli/). When a question is about SDK behaviour rather
than this plugin's own code, look there; the SDK assemblies ship without XML docs, so where a page is
missing, reflect over the package instead of guessing.

## Before you start on a fresh plugin

`dotnet new macrodeck-plugin` sets the identity and the publication metadata for you:

```bash
dotnet new macrodeck-plugin -n <Name> --pluginId <id> --pluginName "<Display name>" \
  --publisher "<Publisher>" --repository <url> --platforms win-x64 --platforms osx-arm64
```

`--publisher`, `--description`, `--license`, `--repository`, `--homepage` and `--platforms` all land in
`manifest.json` natively, and `--platforms` drives `macrodeck-build.json` with it. `--repository` and
`--homepage` are omitted rather than written empty when not supplied, because the schema requires an
absolute URL. `macrodeck-plugin new` collects the same values and passes them through.

A repository *cloned* from this template still carries the template's identity, so fix that first, in
one change:

1. `manifest.json` - `id` (reverse-domain, lowercase, at least two dot-joined kebab segments, e.g.
   `com.example.my-plugin`), `name`, `version`, `description`, `publisher.name`, and `entrypoints` plus
   the matching `macrodeck-build.json` targets for the platforms you actually ship.
2. Rename the project, the test project, the solution file and the namespace. The project's
   `AssemblyName` and `RootNamespace` are pinned: the first names the executable, so change it together
   with the `entrypoints` paths - never one without the other, or `macrodeck-plugin build` fails with
   `entrypoint-missing`; the second is where the generated `Strings` class lives.
3. Replace `Assets/icon.svg`. The manifest's `icon` path is the single source of truth and the host
   reads that file directly - there is no icon code to change.
4. Replace `LogMessageAction` with the plugin's real first action, and its keys in
   `Localization/Strings.resx` with real ones.

`MacroDeck.Plugin.Analyzers` is already referenced with `PrivateAssets="all"` - 13 compile-time
diagnostics that catch most of the mistakes below while you type, plus the `[MacroDeckSdkUsage]`
attribute the host reads to report real deprecation usage instead of inferring it. Keep it.

## Store gate

This plugin is published through the Macro Deck Store, and the Creator Portal refuses a build that
breaks its policy. The policy changes over time, so never rely on a copy, on memory or on an earlier
fetch in the same session. **Fetch every document below again before you start a change and again before
you call it done.** If one cannot be fetched, say so and stop rather than assuming it still says what it
said last time.

| What                                                       | Where                                                                         |
| ---------------------------------------------------------- | ----------------------------------------------------------------------------- |
| Creator Guidelines (Markdown)                              | <https://api.macro-deck.app/api/v1/public/creator-guidelines>                 |
| Blocked packages (JSON)                                    | <https://api.macro-deck.app/api/v1/public/dependency-policy/blocked-packages> |
| Minimum SDK version and allowed Macro Deck packages (JSON) | <https://api.macro-deck.app/api/v1/public/dependency-policy/sdk>              |

A change is not done until all six hold:

1. **The plugin follows the Creator Guidelines.** Read the whole document, not just the part that seems
   relevant, and check the change against every rule in it. Where a rule here and the guidelines
   disagree, the guidelines win. Point out the conflict so this file can be corrected.
2. **No blocked package is used.** Check every entry in `blockedPackages` against the full dependency
   graph (`dotnet list package --include-transitive`), not only the packages referenced directly (this
   includes `HidSharp`). An entry matches by `packagePattern`. A `versionPattern` of `null` blocks every
   version. Otherwise only the matching versions are blocked. `reason` says why. Replace the package or
   restructure the code that needs it. Never work around a block by vendoring, renaming or loading the
   package some other way.
3. **The Macro Deck SDK is at least `minimumSdkVersion`.** The Macro Deck packages
   use the exact version in `Directory.Packages.props`. Bump it with `dotnet package update` before a
   release that needs newer SDK surface, and never release with a `3.0.0-local.N` version from
   `local-feed/`. A release must never be built against an SDK older than the minimum.
4. **Only allowed Macro Deck packages are used.** Every package in the graph whose id starts with
   `MacroDeck.` must match an entry in `allowedMacroDeckPackages`. Anything else under that prefix is
   refused on upload, including packages from another Macro Deck repository that were never published
   for plugins.
5. **The manifest names its author, licence and repository.** `publisher.name` in `manifest.json` must
   be the owner the plugin is published under in the Creator Portal: the Organization's name, or for a
   personal Project the creator's username (compared ignoring case). The Store always shows that owner,
   and an upload whose manifest names anyone else is refused. `license` must be set, at most 64
   characters, as an SPDX identifier such as `MIT`; the Store shows it as the plugin's licence.
   `repository` must be the GitHub repository the plugin is built and released from, written
   `https://github.com/<owner>/<name>`; an upload from any other repository is refused.
6. **The plugin passes the conformance suite on every platform it declares.** The Store refuses a build
   that does not come with a conformance report for each platform in `entrypoints`, or whose report shows
   a failed Required check, never reached the plugin (`MDC0201`, the handshake, did not pass), or is for
   another plugin id or version. The official publishing workflow runs the suite on a stub host per
   platform and sends the reports, so never turn its `run-stub-host` off. Before calling a change done,
   run `macrodeck-plugin test` (see "Verifying a change") and fix every Required failure; a failed
   Recommended check is allowed but worth fixing.

Report the result of this gate with every change: which version of the guidelines was checked (the
`X-Creator-Guidelines-Version` response header), and whether each of the six points holds.

## The rules that make a plugin clean

### Identity

- A plugin's identity is `manifest.json` and nothing else. `IPluginIntegration` carries no `Id`, `Name`,
  `Version` or `IsInitialized`, and it never implements `IIntegrationIconProvider` - both of those are
  the in-process `IIntegration`'s surface. The builder's old `WithId`/`WithName`/`WithVersion`/
  `WithDescription`/`WithIcon` are gone; do not reintroduce them.
- Ids you write in source are **local ids** - `^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$`, max 64 chars, never
  containing `::`. The host derives the qualified `integrationId::localId` form from your authenticated
  registration. Never build or submit a qualified id yourself.
- **Action ids must be unique across the whole plugin**, not just per integration. In process the key is
  (integration id, action id); over the wire the owner is the plugin. A collision fails `Build()`.
- Event definition ids and variable `DefinitionId`s are persisted in user data. Treat them as a public
  API: renaming one breaks every widget already bound to it.

### The builder and the process

- Leave `Program.cs` shaped as it is: `CreatePlugin(args)` → `.UseMacroDeckLogging()` →
  `.UseLocalization(Strings.LocalizationCatalog)` → `.RegisterIntegration<T>()` → `Build()` →
  `RunAsync()`. It is a real `WebApplication` builder, so
  `IHttpClientFactory`, options binding, hosted services and DI are all available and preferred over
  hand-rolled equivalents. Extra registrations go on `builder.Services` before `Build()`.
- `RegisterIntegration<T>()` is the one door: it also registers the capability handler for every SDK
  interface the integration implements, so you never write a handler for a built-in capability kind. A
  genuinely new kind uses `ICapabilityHandler` + `RegisterCapabilityHandler<T>()`.
- `services.AddMacroDeckIntegration<T>()` is internal to the hosting package now and cannot be called
  from a plugin project. Registering the integration with `services.AddSingleton<T>()` is an analyzer
  warning, not a workaround.
- `Build()` constructs every integration and handler as part of validation, so a constructor must be
  side-effect-free and cheap.
- **Never set your own listener URL.** No `UseUrls`, no `Configuration["urls"]`, no `ASPNETCORE_URLS` in
  `launchSettings.json` or `appsettings.json`. The supervisor binds a port and starts probing
  `GET /_macrodeck/health` before your process starts; overriding it makes the health check fail silently
  and permanently, with nothing surfaced to explain why.
- `/_macrodeck/*` is reserved. Mapping a route under it fails `Build()`.
- The only writable location to rely on is `MACRO_DECK_PLUGIN_DATA_DIRECTORY`. Anything written next to
  the executable lives in an immutable version directory and disappears on the next update or rollback.
- `Build()` reports **every** local problem at once in one `PluginConfigurationException`. Read the whole
  message before fixing anything; do not iterate one error per run.

### Lifecycle

`InitializeAsync` does not run at process start. It is gated on the connection being established, and it
runs again after a non-resume reconnect and whenever the host reports a configuration change. Make it
idempotent and safe to run repeatedly against an already-initialized process.

### Actions

- `ActionResult` must be truthful. `Success()` claims the operation completed. Could not reach the
  provider, refused a permission, handed an unusable value → `Failed(code, message)` with the closest
  `ActionErrorCodes` value. A press that did nothing must never report success.
- A legitimate no-op *is* success: an optional parameter left blank, a repeat count of zero, a setting
  already in the requested state.
- `Accepted` is only for work the provider took but cannot confirm. Where the API *can* confirm, poll
  until it does rather than returning `Accepted`.
- Throwing works (the flow engine records a failure) but the caller only ever sees a generic code - an
  exception message is never sent to a client. Prefer an explicit `Failed`.
- A synchronous executor returns the cached `ActionResult.SucceededTask` rather than allocating.
- **Forward `context.CancellationToken`** into everything you await. Dropping it is MDP3001.
- Parameter visibility (`OnlyWhen`) is presentation only. The host still sends hidden parameters, so
  validate the combination in the executor - never infer anything from a field being hidden.

### Async and concurrency

- No `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` or `Thread.Sleep` anywhere in a type implementing
  `ICapabilityHandler`, `IActionExecutor` or `IConfigFlow` - the rule is whole-type, not just the
  interface methods, because the dispatcher has 32 concurrent invocation slots and a block anywhere
  reachable starves the rest (MDP3002).
- No `async void` on an SDK contract type; an exception there kills the process instead of failing one
  call. The `(object? sender, EventArgs e)` handler shape is the one exception (MDP3003).
- Invocations dispatch **concurrently**, each in its own DI scope. Instance state touched from more than
  one invocation needs its own synchronization - the same discipline as any concurrently invoked ASP.NET
  Core endpoint.
- `ICapabilityInvocationContext` only resolves inside an invocation scope. A singleton must not depend on
  it (MDP4001).

### Fire-and-forget contracts

`IEventPublisher.Publish`, `IUserNotifier.Notify`/`Dismiss` and `IPluginCatalogNotifier.CatalogChanged`
never throw and are safe to call with no live session. Mirror that in your own wrappers: they are called
from websocket callbacks and poll loops where a throw would take the integration's own work down.

The round-trip host callbacks - `context.Variables`, `context.Config`, `context.UserVariables`,
`context.Deck`'s mutating members, `context.Scripts.RunAsync`, `context.Widgets.ApplyAsync` - are the
opposite: real network calls that can throw `HostInvocationException` on rate limiting, timeout or no
connection. Handle them like any networked call. `Deck.GetFolders()`, `Scripts.GetScripts()` and
`Widgets.GetWidgets()` read a host-pushed cache instead and return empty in the short window before the
first push.

### Catalogues go stale - say so

Every synchronous catalog-shaped member (`GetInstances`, `EventDefinitions`, `DeclaredVariables`,
`GetProfiles`, …) is served to the host from a cached `describe`, not a live call. When something outside
a host-initiated invocation changes what a later `describe` would answer - a config value
`InitializeAsync` just read, a device that appeared or vanished - inject `IPluginCatalogNotifier` and call
`CatalogChanged(kind, reason)`. Skipping it leaves the UI disagreeing with the plugin until the next
reconnect. The host describes capabilities concurrently with `InitializeAsync`, so a first describe can
capture a default before the config read finishes.

**But never call `CatalogChanged` from inside `InitializeAsync`, or from anything it triggers
synchronously (an event handler `_catalog.Set` raises, a poll it kicks).** With
`VariablesDependOnConfiguration => true` the host already re-pulls the catalogue after a config
change; a notification landing *during* the reinit makes it unregister and re-register the whole
integration, which re-enters `InitializeAsync`, which notifies again - an 8-deep loop that ends with
every provided variable failing to register as `AlreadyExists` (net: zero variables, and the
localization + widget-type registration churned so strings render as `[[plugin:<id>:Key]]` and the
widgets vanish). `BatteryIntegration` therefore takes no `IPluginCatalogNotifier` at all
(`CatalogNotificationTests` asserts that): a device-set change only re-polls, and the on-demand variable
catalogue is resolved live. Integration issues need no notification either, because the host lists them
with a live call on its own schedule (confirmed: the Linux HID issue appears without one).

### Configuration and secrets

- A config flow produces the entries; `InitializeAsync` reads them back through
  `IIntegrationContext.Config`. Keep that division - the flow validates and persists, the integration
  consumes.
- Persist credentials as `ConfigFlowValue.Secret` so they land in the host's encrypted secret store.
  Never write a token to a plain string field, a log line, or a file of your own. Rotating credentials go
  back through `SetSecretAsync`.
- Never run your own OAuth redirect server. Return `ConfigFlowResult.External(url, resumeStepId)` and let
  the host own the redirect and the callback correlation.
- An integration that provides a config flow starts **disabled** until the user completes it. Everything
  else starts enabled.

### Localization

- **No user-facing literal.** Every string a user reads is a key in `Localization/Strings.resx`, reached
  through the generated `Strings` class: action names and descriptions, parameter labels, descriptions
  and placeholders, `ActionStateDefinition` labels, config flow step titles and field labels, event and
  variable metadata, issue text, and the message on `ActionResult.Failed`/`Accepted`. A plain `string`
  converts to `LocalizedText` too, so nothing stops you - which is exactly why this is a rule.
- `ConfigFlowResult.Complete(title, …)` is the one deliberate exception: it takes a plain `string`
  because the host stores it as the configured entry's name and the user renames it from there. Write it
  in the default language and leave it.
- Log messages and exception messages are diagnostics, not UI. They stay English literals; never
  localize a log template.
- Check `MacroDeckStrings` before adding a key - `Common.*`, `Validation.*`, `Connection.*`,
  `Settings.*` are already translated everywhere Macro Deck ships. A duplicated `Save` is one more string
  every translator keeps in sync for text the reader already sees. Compose instead:
  `MacroDeckStrings.Validation.Required(Strings.Actions.LogMessage.Message.Label())`.
- **Keys are dotted, not underscored.** A dotted key becomes a nested class, so
  `Actions.LogMessage.Message.Label` is `Strings.Actions.LogMessage.Message.Label()`. Name a key after
  where it is used, not after the English wording. A key that is also a group other keys nest under is
  MDLOC008.
- Placeholders are named (`{host}`) and become method parameters, so a missing one is a compile error.
  Positional `{0}` gives no such safety. A placeholder is a `string` unless a bracketed prefix on the
  entry's `<comment>` narrows it to `int`, `long`, `double` or `bool`.
- A count-dependent sentence is one key: `[plural]` on **every** form, keys suffixed `.One` and `.Other`,
  `Other` required. The rule is `count == 1` for every language and deliberately not CLDR, so phrase
  `Other` to stay grammatical where that rule does not hold.
- A translation is `Localization/Strings.<culture>.resx` with a well-formed BCP-47 name (`de`, `pt-BR`,
  `zh-Hant-TW`, never a truncated `zh`, never an underscore). It needs only the keys it translates; the
  chain falls back requested culture → neutral → catalog default → `en`. Nothing is registered per
  language.
- `Strings.resx` itself is required even for a single-language plugin: it is what every translation is
  checked against and the last resource the chain tries.
- The manifest's `languages` array is derived by `macrodeck-plugin build` and `pack` from this folder.
  Hand-maintaining it is the same mistake as hand-maintaining `files[]`.
- The generator's own diagnostics are MDLOC001-MDLOC008 (key only in a translation, placeholder
  mismatch, duplicate key, bad parameter type, malformed culture suffix, removed `MacroDeckStrings` key,
  broken plural family, key/group collision). Fix them; do not suppress them.
- Dropping `UseLocalization(Strings.LocalizationCatalog)` from `Program.cs` does not fail the build. It
  fails at runtime, quietly, with every label rendering as `[[plugin:<id>:Key]]`.
- The full reference is <https://docs.macro-deck.app/sdk/localization/>.

### Logging

- Log through **Serilog**, not `Microsoft.Extensions.Logging`. Inject Serilog's `ILogger` (and
  `.ForContext<T>()`), or use `IntegrationLog`. `UseMacroDeckLogging()` forwards everything to the host's
  log viewer.
- The host stamps integration identity from the authenticated session - a plugin cannot set its own
  attribution, so do not try.
- Logging is rate-limited and a flood is dropped. For a poll loop that fails repeatedly, use
  `FailureEpisodeTracker` rather than a line per tick or a silent Debug-only degrade.
- Structured properties reach the live viewer but are not persisted to the log file - put anything that
  must survive into the message template.

### Comments and style

- `Directory.Build.props` sets `Nullable`, `ImplicitUsings`, `latest-recommended` analysis,
  `EnforceCodeStyleInBuild` and `CS8602` as an error. Build warning-free; do not relax these to make a
  build pass.
- C# is indented with four spaces. Match the surrounding file rather than reformatting it.
- Suppress a diagnostic with the narrowest scope that fits and **always with a reason** on the
  `#pragma` or the `NoWarn` entry.
- Comments explain non-obvious constraints - a race, a protocol rule, why a shape was chosen - not what
  the code already says.
- Write comments in English, regardless of the language used in chat or commit discussion.
- No decorative comment formatting - no ASCII dividers, banners, box-drawing, or emoji. A comment is a
  plain sentence, not a header.
- Write a comment as one or two plain `//` lines directly above the code it explains. No `<summary>`
  blocks on internal types or members and none on tests unless the setup is non-obvious. A type or member
  whose name already says it needs none.
- Keep comments as short as the constraint allows, and prefer no comment at all. Only write one for
  genuinely non-obvious or complex logic (a race, a workaround, a protocol quirk) - never to restate what
  a well-named method or property already says.
- Method and property names must be self-explanatory. If a name needs a comment to be understood, rename
  it instead of documenting it.
- No em dashes in code, comments, commit messages or documentation - use a period, comma or parenthesis
  instead.

### Code quality

- Follow DRY: extract shared logic instead of duplicating it across actions, providers or config flow
  steps. Do not extract on the first occurrence of similar code; do so once a real duplication pattern
  emerges.
- Keep a clean architecture: respect the separation between the integration, actions, config flow and
  provider-specific code. Don't reach across those boundaries or leak provider-specific types into
  shared SDK-facing surfaces.
- Consider performance: avoid unnecessary allocations, synchronous blocking, or repeated expensive work
  in hot paths (invocation handlers, poll loops).
- Dispose everything that owns unmanaged or long-lived resources (`HttpClient` handlers, subscriptions,
  timers, cancellation token registrations, websocket connections). Prefer `IAsyncDisposable`/`IDisposable`
  and DI-managed lifetimes over manual lifecycle management. Watch for event-handler subscriptions that
  outlive their subscriber - a common source of memory leaks in long-running plugin processes.

## Verifying a change

Run the ones that apply, in this order, before calling a change done:

```bash
dotnet build
```

```bash
dotnet test
```

For an interactive verification, start the installed Macro Deck desktop app and debug the plugin with
the **Macro Deck - Real Host** `.NET` launch profile. Keep the profile secret-free; supply a first-run
enrollment token only through the project's local .NET User Secrets and remove it after registration.
Do not add a second run configuration or a CLI/executable startup path.

```bash
macrodeck-plugin test --project src/DeviceBatteryInfo --report markdown --output conformance.md
```

The conformance suite drives a real session: capability contracts, invocation and cancellation semantics,
reconnect and resume, the reserved endpoints, logging limits. Exit `0` conformant, `1` the plugin is
wrong, `2` usage error, `3` input unreadable, `4` cancelled - `1` and `3` are deliberately distinct. Run
it after any change to capability shape, cancellation handling or the manifest, and treat a Required
check going from pass to fail as a blocking regression. Most checks `SKIP` until the plugin declares
capabilities.


Working in the template repository itself rather than in a plugin generated from it? Changing its shape
(files, names, `.template.config/template.json`, `packaging/`) also needs a generated-project check -
see
[packaging/README.md](https://github.com/Macro-Deck-App/Macro-Deck-Plugin-Template/blob/main/packaging/README.md).

## Packing a plugin release

```bash
macrodeck-plugin build --source src/DeviceBatteryInfo --output ./artifacts
macrodeck-plugin inspect --artifact ./artifacts/<id>-<version>.macroDeckPlugin
```

`build` reads `macrodeck-build.json`, publishes each runtime identifier the manifest declares into its
`runtimes/<rid>/` slot and packs the result. `--rid <rid>` builds one platform, for a CI matrix job.

A `dotnet build -c Release` output is *not* packable: the manifest points at `runtimes/<rid>/`, which
only `build` assembles, so `validate`/`pack` against `bin/Release/net10.0` fails on a missing entrypoint.
Adding a platform means adding it to `entrypoints` **and** `macrodeck-build.json`.

This plugin is **framework-dependent**: `entrypoints.win-x64`, `osx-arm64` and `linux-x64` each name
`runtimes/<rid>/DeviceBatteryInfo.dll` (the same managed build, no native assets) with `"runtime": {
"kind": "FrameworkDependent", "dotnetVersion": "10.0" }`, and `macrodeck-build.json` publishes with
`--self-contained false -p:UseAppHost=false`. Macro Deck ships a .NET 10 runtime (ASP.NET Core included)
with the host and runs the plugin on it, which is also why it appears as `dotnet` in process lists.
Self-contained is the other pairing, for a runtime Macro Deck does not ship: drop the `runtime` block,
point `executable` at the apphost (no `.dll`; `.exe` on Windows) and publish with `--self-contained true`
(no `-p:UseAppHost=false`). Keep the manifest and `macrodeck-build.json` on the same pairing; mixing them
fails validation.

Packing validates first, recomputes every `files[]` digest from disk and fills in `languages` from
`Localization/`, discarding whatever the source manifest declared - so never hand-maintain either.
Signing happens *after* packing, against the packed manifest; sign earlier and the digest will not
match.

## Workflow

- Work on a branch, not directly on `main`. Use `feature/`, `fix/`, `refactor/`, `chore/`, `docs/` or
  `ci/` with a short kebab-case description, and an issue number where one exists.
- Publishing the template package requires a pushed semantic-version tag such as
  `v3.0.0-preview.3`. The publish workflow removes the leading `v` and uses the rest as the NuGet
  package version. A push to `main` alone never publishes.
- Keep changes focused; no unrelated reformatting.
- Do not push or open a pull request unless asked.
- Do not add AI attribution or co-author trailers.
- Update `README.md` when the build, run, packaging or capability story changes. Update this file when a
  rule here stops being true.
