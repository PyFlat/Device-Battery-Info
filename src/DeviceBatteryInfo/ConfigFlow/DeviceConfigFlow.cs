using DeviceBatteryInfo.Core;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using Serilog;

namespace DeviceBatteryInfo.ConfigFlow;

internal sealed class DeviceConfigFlow(
    IDeviceDiscovery discovery,
    DeviceModelCatalog models,
    IReadOnlyList<BatterySlot> currentDevices,
    ILogger logger
) : IConfigFlow
{
    private const string StepBasics = "basics";
    private const string StepOther = "other";
    private const string StepDetails = "details";

    private const string AddingADeviceDocUrl =
        "https://github.com/PyFlat-JR/Device-Battery-Info/blob/main/docs/adding-a-device.md";

    private static readonly TimeSpan DiscoveryBudget = TimeSpan.FromSeconds(5);

    private static readonly string[] BluetoothKinds =
    [
        "mouse",
        "keyboard",
        "headset",
        "earbuds",
        "phone",
        "tablet",
        "controller",
        "pen",
        "other",
    ];

    private readonly Dictionary<string, string?> _draft = new(StringComparer.Ordinal);

    public Task<ConfigFlowResult> StartAsync(
        IConfigFlowContext context,
        CancellationToken cancellationToken
    )
    {
        var editing = FindEditing(context);
        if (editing is not null)
        {
            SeedDraftFrom(editing);
        }

        logger.Information(
            "Device config started (editing={Editing}).",
            editing?.DisplayName ?? "<new>"
        );
        return Task.FromResult(ConfigFlowResult.Step(BuildBasicsStep(editing)));
    }

    public async Task<ConfigFlowResult> SubmitAsync(
        string stepId,
        IReadOnlyDictionary<string, object?> input,
        IConfigFlowContext context,
        CancellationToken cancellationToken
    )
    {
        Merge(input);

        var editing = FindEditing(context);
        var name =
            Draft(DeviceConfigKeys.Name)
            ?? (context as IConfigFlowEntryContext)?.EntryTitle
            ?? string.Empty;
        _draft[DeviceConfigKeys.Name] = name;
        var type = ResolveType();

        switch (stepId)
        {
            case StepBasics:
            {
                if (name.Length == 0)
                {
                    return ConfigFlowResult.Error(
                        BuildBasicsStep(editing),
                        Strings.ConfigFlow.Device.FixFields(),
                        new Dictionary<string, LocalizedText>
                        {
                            [DeviceConfigKeys.Name] = MacroDeckStrings.Validation.Required(
                                Strings.ConfigFlow.Device.Name.Label()
                            ),
                        }
                    );
                }

                if (Draft(DeviceConfigKeys.Category) == DeviceConfigKeys.CategoryOther)
                {
                    return ConfigFlowResult.Step(BuildOtherStep(editing));
                }

                return type == DeviceType.System
                    ? Complete(name, type)
                    : ConfigFlowResult.Step(
                        await BuildDetailsStepAsync(name, type, editing, cancellationToken)
                    );
            }

            case StepOther:
            {
                var brand = Draft(DeviceConfigKeys.CatalogBrand);
                var entry = models.ById(
                    brand is null ? null : Draft(CatalogDeviceKey(brand))
                );

                if (entry is null || entry.BrandId != brand)
                {
                    return ConfigFlowResult.Error(
                        BuildOtherStep(editing),
                        Strings.ConfigFlow.Device.FixFields(),
                        new Dictionary<string, LocalizedText>
                        {
                            [DeviceConfigKeys.CatalogDevice] =
                                Strings.ConfigFlow.Device.Other.Model.Invalid(),
                        }
                    );
                }

                _draft[DeviceConfigKeys.CatalogDevice] = entry.Id;

                return Complete(name, DeviceType.Catalog);
            }

            case StepDetails:
            {
                var errors = ValidateDetails(type);
                if (errors.Count > 0)
                {
                    return ConfigFlowResult.Error(
                        await BuildDetailsStepAsync(name, type, editing, cancellationToken),
                        Strings.ConfigFlow.Device.FixFields(),
                        errors
                    );
                }

                return Complete(name, type);
            }

            default:
                return ConfigFlowResult.Error(
                    BuildBasicsStep(editing),
                    Strings.ConfigFlow.Device.UnknownStep()
                );
        }
    }

    private Dictionary<string, LocalizedText> ValidateDetails(DeviceType type)
    {
        var errors = new Dictionary<string, LocalizedText>();

        switch (type)
        {
            case DeviceType.AdbPhone when Draft(DeviceConfigKeys.AdbAddress) is not { Length: > 0 }:
                errors[DeviceConfigKeys.AdbAddress] = MacroDeckStrings.Validation.Required(
                    Strings.ConfigFlow.Device.AdbAddress.Label()
                );
                break;

            case DeviceType.Bluetooth when ResolvedBluetoothName() is not { Length: > 0 }:
                errors[DeviceConfigKeys.BluetoothName] = MacroDeckStrings.Validation.Required(
                    Strings.ConfigFlow.Device.BluetoothName.Label()
                );
                break;
        }

        return errors;
    }

    private ConfigFlowResult Complete(string name, DeviceType type)
    {
        var values = new Dictionary<string, ConfigFlowValue>(StringComparer.Ordinal)
        {
            [DeviceConfigKeys.Name] = ConfigFlowValue.Plain(name),
            [DeviceConfigKeys.Type] = ConfigFlowValue.Plain(DeviceConfigKeys.TypeValue(type)),
        };

        switch (type)
        {
            case DeviceType.Catalog:
                values[DeviceConfigKeys.CatalogDevice] = ConfigFlowValue.Plain(
                    Draft(DeviceConfigKeys.CatalogDevice)
                );
                break;

            case DeviceType.AdbPhone:
                values[DeviceConfigKeys.AdbAddress] = ConfigFlowValue.Plain(
                    Draft(DeviceConfigKeys.AdbAddress)
                );
                values[DeviceConfigKeys.AdbExecutable] = ConfigFlowValue.Plain(
                    Draft(DeviceConfigKeys.AdbExecutable) ?? "adb"
                );
                break;

            case DeviceType.Bluetooth:
                values[DeviceConfigKeys.BluetoothName] = ConfigFlowValue.Plain(
                    ResolvedBluetoothName()
                );
                values[DeviceConfigKeys.BluetoothKind] = ConfigFlowValue.Plain(
                    Draft(DeviceConfigKeys.BluetoothKind) ?? "headset"
                );
                break;
        }

        logger.Information(
            "Device config complete: {Name} ({Type}).",
            name,
            DeviceConfigKeys.TypeValue(type)
        );
        return ConfigFlowResult.Complete(name, values);
    }

    private ConfigFlowStep BuildBasicsStep(BatterySlot? editing)
    {
        var editingCategory = editing is null
            ? null
            : DeviceConfigKeys.TypeToCategory(editing.Type);

        var categoryOptions = new[]
        {
            new ActionParameterOption
            {
                Value = DeviceConfigKeys.TypeAdbPhone,
                Label = Strings.ConfigFlow.Device.Category.AdbPhone(),
            },
            new ActionParameterOption
            {
                Value = DeviceConfigKeys.TypeBluetooth,
                Label = Strings.ConfigFlow.Device.Category.Bluetooth(),
            },
            new ActionParameterOption
            {
                Value = DeviceConfigKeys.TypeSystem,
                Label = Strings.ConfigFlow.Device.Category.System(),
            },
            new ActionParameterOption
            {
                Value = DeviceConfigKeys.CategoryOther,
                Label = Strings.ConfigFlow.Device.Category.Other(),
            },
        };

        return new ConfigFlowStep
        {
            StepId = StepBasics,
            Title = editing is null
                ? Strings.ConfigFlow.Device.Title()
                : Strings.ConfigFlow.Device.EditTitle(editing.DisplayName),
            Description = Strings.ConfigFlow.Device.Description(),
            Fields =
            [
                ActionParameter.Text(
                    DeviceConfigKeys.Name,
                    label: Strings.ConfigFlow.Device.Name.Label(),
                    defaultValue: Draft(DeviceConfigKeys.Name) ?? editing?.DisplayName,
                    required: true
                ),
                ActionParameter.Choice(
                    DeviceConfigKeys.Category,
                    categoryOptions,
                    label: Strings.ConfigFlow.Device.Category.Label(),
                    defaultValue: Draft(DeviceConfigKeys.Category)
                        ?? editingCategory
                        ?? DeviceConfigKeys.TypeAdbPhone,
                    required: true
                ),
            ],
        };
    }

    private ConfigFlowStep BuildOtherStep(BatterySlot? editing)
    {
        var editingEntry = editing is null ? null : models.For(editing);
        var brands = models.Brands;
        var selectedBrand =
            Draft(DeviceConfigKeys.CatalogBrand) ?? editingEntry?.BrandId ?? brands[0].Id;

        var fields = new List<ActionParameter>
        {
            ActionParameter.Choice(
                DeviceConfigKeys.CatalogBrand,
                brands
                    .Select(b => new ActionParameterOption { Value = b.Id, Label = b.Label })
                    .ToArray(),
                label: Strings.ConfigFlow.Device.Other.Brand.Label(),
                defaultValue: selectedBrand,
                required: true
            ),
        };

        foreach (var (brandId, _) in brands)
        {
            var brandModels = models.ModelsFor(brandId);
            var defaultModel =
                Draft(CatalogDeviceKey(brandId))
                ?? (editingEntry?.BrandId == brandId ? editingEntry.Id : null)
                ?? brandModels[0].Id;

            fields.Add(
                ActionParameter
                    .Choice(
                        CatalogDeviceKey(brandId),
                        brandModels
                            .Select(m => new ActionParameterOption
                            {
                                Value = m.Id,
                                Label = m.Name,
                            })
                            .ToArray(),
                        label: Strings.ConfigFlow.Device.Other.Model.Label(),
                        defaultValue: defaultModel,
                        required: true
                    )
                    .OnlyWhen(DeviceConfigKeys.CatalogBrand, brandId)
            );
        }

        return new ConfigFlowStep
        {
            StepId = StepOther,
            Title = Strings.ConfigFlow.Device.Other.Title(),
            Description = Strings.ConfigFlow.Device.Other.Description(),
            Fields = fields,
            Links =
            [
                new ConfigFlowLink
                {
                    Label = Strings.ConfigFlow.Device.Other.AddMissingLink(),
                    Url = AddingADeviceDocUrl,
                },
            ],
        };
    }

    private async Task<ConfigFlowStep> BuildDetailsStepAsync(
        string name,
        DeviceType type,
        BatterySlot? editing,
        CancellationToken cancellationToken
    )
    {
        var fields = new List<ActionParameter>();
        var advanced = new List<ActionParameter>();

        switch (type)
        {
            case DeviceType.AdbPhone:
                fields.Add(
                    ActionParameter.Text(
                        DeviceConfigKeys.AdbAddress,
                        label: Strings.ConfigFlow.Device.AdbAddress.Label(),
                        placeholder: "192.168.1.42:5555",
                        defaultValue: Draft(DeviceConfigKeys.AdbAddress),
                        required: true
                    )
                );
                advanced.Add(
                    ActionParameter.Text(
                        DeviceConfigKeys.AdbExecutable,
                        label: Strings.ConfigFlow.Device.AdbExecutable.Label(),
                        defaultValue: Draft(DeviceConfigKeys.AdbExecutable) ?? "adb"
                    )
                );
                break;

            case DeviceType.Bluetooth:
            {
                var bluetoothDevices = await ListBluetoothDevicesAsync(cancellationToken);
                var options = bluetoothDevices.Select(BluetoothNameOption).ToArray();
                fields.Add(BluetoothNameField(options));
                if (
                    options.Length > 0
                    && Draft(DeviceConfigKeys.BluetoothName) is not { Length: > 0 }
                )
                {
                    advanced.Add(BluetoothNameCustomField());
                }

                fields.Add(
                    ActionParameter.Choice(
                        DeviceConfigKeys.BluetoothKind,
                        BluetoothKinds
                            .Select(k => new ActionParameterOption { Value = k, Label = k })
                            .ToArray(),
                        label: Strings.ConfigFlow.Device.BluetoothKind.Label(),
                        defaultValue: Draft(DeviceConfigKeys.BluetoothKind) ?? "headset"
                    )
                );
                break;
            }
        }

        return new ConfigFlowStep
        {
            StepId = StepDetails,
            Title = Strings.ConfigFlow.Device.DetailsTitle(
                name.Length > 0 ? name : editing?.DisplayName ?? string.Empty
            ),
            Description = Strings.ConfigFlow.Device.DetailsDescription(),
            Fields = fields,
            AdvancedFields = advanced,
        };
    }

    private static ActionParameterOption BluetoothNameOption(BluetoothDeviceCandidate device) =>
        new()
        {
            Value = device.Name,
            Label = device.Percent is { } percent
                ? Strings.ConfigFlow.Device.BluetoothName.OptionWithBattery(
                    device.Name,
                    $"{percent}%"
                )
                : Strings.ConfigFlow.Device.BluetoothName.OptionWithoutBattery(device.Name),
        };

    private ActionParameter BluetoothNameCustomField() =>
        ActionParameter.Text(
            DeviceConfigKeys.BluetoothNameCustom,
            label: Strings.ConfigFlow.Device.BluetoothNameCustom.Label(),
            description: Strings.ConfigFlow.Device.BluetoothNameCustom.Description(),
            defaultValue: Draft(DeviceConfigKeys.BluetoothNameCustom)
        );

    private string? ResolvedBluetoothName() =>
        Draft(DeviceConfigKeys.BluetoothNameCustom) ?? Draft(DeviceConfigKeys.BluetoothName);

    private ActionParameter BluetoothNameField(ActionParameterOption[] options)
    {
        var current = Draft(DeviceConfigKeys.BluetoothName);

        if (current is { Length: > 0 } || options.Length == 0)
        {
            return ActionParameter.Text(
                DeviceConfigKeys.BluetoothName,
                label: Strings.ConfigFlow.Device.BluetoothName.Label(),
                description: Strings.ConfigFlow.Device.BluetoothName.Description(),
                defaultValue: current
            );
        }

        return ActionParameter.Choice(
            DeviceConfigKeys.BluetoothName,
            options,
            label: Strings.ConfigFlow.Device.BluetoothName.Label(),
            description: Strings.ConfigFlow.Device.BluetoothName.ListDescription(),
            defaultValue: options[0].Value,
            required: true
        );
    }

    private void SeedDraftFrom(BatterySlot slot)
    {
        _draft[DeviceConfigKeys.Name] = slot.DisplayName;
        _draft[DeviceConfigKeys.Category] = DeviceConfigKeys.TypeToCategory(slot.Type);
        _draft[DeviceConfigKeys.AdbAddress] = slot.AdbAddress;
        _draft[DeviceConfigKeys.AdbExecutable] = slot.AdbExecutable;
        _draft[DeviceConfigKeys.BluetoothName] = slot.BluetoothFriendlyName;
        if (slot.Type == DeviceType.Bluetooth)
        {
            _draft[DeviceConfigKeys.BluetoothKind] = DeviceConfigKeys.KindValue(slot.Kind);
        }

        if (models.For(slot) is { } catalogEntry)
        {
            _draft[DeviceConfigKeys.CatalogBrand] = catalogEntry.BrandId;
            _draft[CatalogDeviceKey(catalogEntry.BrandId)] = catalogEntry.Id;
        }
    }

    private static string CatalogDeviceKey(string brandId) =>
        $"{DeviceConfigKeys.CatalogDevice}:{brandId}";

    private BatterySlot? FindEditing(IConfigFlowContext context)
    {
        var title = (context as IConfigFlowEntryContext)?.EntryTitle;
        return string.IsNullOrEmpty(title)
            ? null
            : currentDevices.FirstOrDefault(d =>
                string.Equals(d.DisplayName, title, StringComparison.Ordinal)
            );
    }

    private void Merge(IReadOnlyDictionary<string, object?> input)
    {
        foreach (var (key, value) in input)
        {
            if (value is string s)
            {
                _draft[key] = s.Length == 0 ? null : s;
            }
            else if (value is null && _draft.ContainsKey(key))
            {
                _draft[key] = null;
            }
        }
    }

    private string? Draft(string key) =>
        _draft.GetValueOrDefault(key) is { Length: > 0 } s ? s : null;

    private DeviceType ResolveType() =>
        Draft(DeviceConfigKeys.Category) == DeviceConfigKeys.CategoryOther
            ? DeviceType.Catalog
            : DeviceConfigKeys.CategoryToType(Draft(DeviceConfigKeys.Category))
                ?? DeviceType.AdbPhone;

    private async Task<IReadOnlyList<BluetoothDeviceCandidate>> ListBluetoothDevicesAsync(
        CancellationToken cancellationToken
    )
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(DiscoveryBudget);

        var work = Guard(
            () => discovery.ListBluetoothDevicesAsync(cts.Token),
            Array.Empty<BluetoothDeviceCandidate>()
        );
        var deadline = Task.Delay(
            DiscoveryBudget + TimeSpan.FromMilliseconds(500),
            CancellationToken.None
        );
        if (await Task.WhenAny(work, deadline).ConfigureAwait(false) != work)
        {
            logger.Warning(
                "Bluetooth discovery did not finish within {Budget}s; serving a text field.",
                DiscoveryBudget.TotalSeconds
            );
            return [];
        }

        return await work.ConfigureAwait(false);
    }

    private static async Task<T> Guard<T>(Func<Task<T>> load, T fallback)
    {
        try
        {
            return await load().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return fallback;
        }
    }
}
