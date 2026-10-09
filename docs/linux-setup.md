# Setting up Device Battery Info on Linux

Most of the plugin works on Linux without any setup: this computer's battery, Android phones through
Macro Deck's adb, and Bluetooth devices. **Only the USB devices from the "Other devices" catalog** (Razer,
Logitech, Corsair, Rapoo, AULA and Sony) need a one-time permission. If Macro Deck shows the problem
*"Linux needs a one-time permission to read a device"* for this plugin, this page is the fix.

## Why it is needed

The plugin asks these devices for their battery level directly over USB, through a file Linux creates for
each of them (`/dev/hidraw0`, `/dev/hidraw1`, ...). By default only root may open those files, so the
plugin can see that your mouse is plugged in but cannot ask it anything.

The fix is a udev rule: a small text file that tells Linux to give **the user logged in at this computer**
access to exactly the supported models listed in it, by their USB product id, and nothing else. Steam,
OpenRGB and similar tools set up their devices the same way. Nothing runs as root, and other users on the
machine get no access.

Keep in mind what the access means: any program you run can then talk to these devices directly. For a
keyboard (the AULA F75) or a receiver that other devices share (the Logitech receiver), that includes
reading what is typed on it. Only install the rule if you use one of these devices with the plugin.

## Install the rule

The whole rule is below, one quoted line each, so you can see exactly what you install. Open a terminal
and paste this block (it works in bash, zsh and fish), which writes it to `/etc/udev/rules.d/` and applies
it:

```bash
printf '%s\n' \
  '# Lets the user logged in at the seat open the hidraw nodes of the devices Device Battery Info reads,' \
  '# and nothing else: one line per supported USB product id, never a whole vendor.' \
  '# The file name must sort before 73-seat-late.rules, which is where the uaccess tag is applied.' \
  '# Install it as described in docs/linux-setup.md.' \
  '' \
  '# AULA F75' \
  'SUBSYSTEM=="hidraw", ATTRS{idVendor}=="3554", ATTRS{idProduct}=="fa09", TAG+="uaccess"' \
  '# Corsair VOID PRO Wireless' \
  'SUBSYSTEM=="hidraw", ATTRS{idVendor}=="1b1c", ATTRS{idProduct}=="0a75", TAG+="uaccess"' \
  '# Logitech G Pro X Wireless' \
  'SUBSYSTEM=="hidraw", ATTRS{idVendor}=="046d", ATTRS{idProduct}=="0aba", TAG+="uaccess"' \
  '# Logitech G502 Lightspeed' \
  'SUBSYSTEM=="hidraw", ATTRS{idVendor}=="046d", ATTRS{idProduct}=="c539", TAG+="uaccess"' \
  'SUBSYSTEM=="hidraw", ATTRS{idVendor}=="046d", ATTRS{idProduct}=="c08d", TAG+="uaccess"' \
  '# Logitech G Pro X Superlight 2' \
  'SUBSYSTEM=="hidraw", ATTRS{idVendor}=="046d", ATTRS{idProduct}=="c54d", TAG+="uaccess"' \
  'SUBSYSTEM=="hidraw", ATTRS{idVendor}=="046d", ATTRS{idProduct}=="c09b", TAG+="uaccess"' \
  '# Rapoo VT3 PRO' \
  'SUBSYSTEM=="hidraw", ATTRS{idVendor}=="24ae", ATTRS{idProduct}=="1215", TAG+="uaccess"' \
  'SUBSYSTEM=="hidraw", ATTRS{idVendor}=="24ae", ATTRS{idProduct}=="4415", TAG+="uaccess"' \
  '# Razer DeathAdder V3 Pro' \
  'SUBSYSTEM=="hidraw", ATTRS{idVendor}=="1532", ATTRS{idProduct}=="00b7", TAG+="uaccess"' \
  'SUBSYSTEM=="hidraw", ATTRS{idVendor}=="1532", ATTRS{idProduct}=="00b6", TAG+="uaccess"' \
  '# Razer Basilisk V3 Pro' \
  'SUBSYSTEM=="hidraw", ATTRS{idVendor}=="1532", ATTRS{idProduct}=="00ab", TAG+="uaccess"' \
  'SUBSYSTEM=="hidraw", ATTRS{idVendor}=="1532", ATTRS{idProduct}=="00aa", TAG+="uaccess"' \
  '# Razer Viper V2 Pro' \
  'SUBSYSTEM=="hidraw", ATTRS{idVendor}=="1532", ATTRS{idProduct}=="00a6", TAG+="uaccess"' \
  'SUBSYSTEM=="hidraw", ATTRS{idVendor}=="1532", ATTRS{idProduct}=="00a5", TAG+="uaccess"' \
  '# Sony DualSense' \
  'SUBSYSTEM=="hidraw", ATTRS{idVendor}=="054c", ATTRS{idProduct}=="0ce6", TAG+="uaccess"' \
  '# Sony DualSense over Bluetooth, which has no USB attributes (bus 0005 in the HID device name)' \
  'SUBSYSTEM=="hidraw", KERNELS=="0005:054C:0CE6.*", TAG+="uaccess"' \
  | sudo tee /etc/udev/rules.d/70-device-battery-info.rules > /dev/null
sudo udevadm control --reload-rules
sudo udevadm trigger --subsystem-match=hidraw
```

Then **unplug the device (or its USB receiver) and plug it back in**. The problem in Macro Deck disappears
on its own at the plugin's next battery read. No restart is needed.

The same rule is in the repository as
[packaging/linux/70-device-battery-info.rules](../packaging/linux/70-device-battery-info.rules). Never
install a udev rule you have not read: a rule can run any program as root.

## Check that it worked

```bash
ls -l /dev/hidraw*
```

The nodes of your device now end in a `+` (for example `crw-rw----+`), which means an extra permission is
attached. `getfacl /dev/hidraw0` should list your user name with `rw-`.

## If it still does not work

- **The rule file name must start with `70-`** (or any number below 73). Linux applies the permission in
  `73-seat-late.rules`, and a rule that runs later has no effect.
- **Your distribution needs systemd** (true for Ubuntu, Fedora, Debian, Arch, openSUSE and most others).
  The rule relies on systemd granting access to the logged-in user, and the Bluetooth support relies on
  systemd's `busctl`.
- **A device connected over Bluetooth** instead of USB is not covered by this rule. Add it as a
  **Bluetooth device** in the plugin instead.
- **Updating the plugin can add new models.** If a newly supported device shows the problem again, run the
  install block once more to get the updated rule.

## Removing it

```bash
sudo rm /etc/udev/rules.d/70-device-battery-info.rules
sudo udevadm control --reload-rules
```
