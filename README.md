# Foot Pedal Keybinder

A driverless keybinding tool for a single-switch iKKEGOL/PCsensor-style USB foot pedal.

The target device is the iKKEGOL `FS2007U1SW` family reporting `VID:3553 PID:B001`. The app is a small Windows C# WinForms app that talks directly to the pedal's HID programming interface using Windows built-in APIs. It does not install the vendor driver and does not use third-party NuGet packages.

## Requirements

- Windows.
- .NET 8 SDK to build from source.
- The foot pedal connected over USB.

## Build and Run

```sh
dotnet run --project native/FootPedalKeybinder/FootPedalKeybinder.csproj
```

Or build a release executable:

```sh
dotnet publish native/FootPedalKeybinder/FootPedalKeybinder.csproj -c Release -r win-x64 --self-contained false
```

## Use

1. Connect the foot pedal.
2. Launch the C# app.
3. Confirm it finds the `3553:B001` programming interface.
4. Click **Capture Key**.
5. Press the key or key combo you want the pedal to send.
6. Click **Write Binding** and confirm the persistent write.
7. Unplug and replug the pedal, then test it in a text box.

## Safety Notes

- The app only writes after you click **Write Binding** and confirm the warning dialog.
- It filters for `VID:3553 PID:B001` and the programming interface marker `MI_01`.
- For the single-switch `FS2007U1SW`, the app writes the captured key to slots 1-3 so the internal slot mapping does not matter.
- On a true multi-pedal unit, writing slots 1-3 would make all pedals send the same key.
- The write is persistent on the pedal, just like the vendor software. Unplugging the pedal will not undo it.

## Protocol Reference

The HID report format is based on the open-source `rgerganov/footswitch` implementation for PCsensor-compatible foot switches, not on the vendor driver. The C# implementation is original code using Windows `hid.dll`, `setupapi.dll`, and `kernel32.dll` through P/Invoke.
