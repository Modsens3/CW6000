# CW6000 ColorWorks Bulk Manager

Windows tools for inspecting and automating Epson ColorWorks CW-C6000Ae media definitions.

## Projects

### CW6000.Inspector
Reads the real Windows UI Automation tree exposed by the Epson driver and exports it to JSON.

### CW6000.BulkManager
Loads label presets from CSV and creates Epson Media Definitions using the verified Automation IDs from driver version 1.10.0.0.

Implemented controls:

- New button: `4502`
- Media Name: `4513`
- Label Width: `4516`
- Label Length: `4519`
- Gap: `4571`
- Left / Right Gap: `4522`
- Media Form: `4527`
- Media Coating: `4529`
- Print Quality: `4531`
- OK: `4542`

## Requirements

- Windows 10 or Windows 11
- .NET 8 SDK
- EPSON CW-C6000Ae driver 1.10.0.0

## Build and run

```powershell
git pull
dotnet build .\src\CW6000.BulkManager\CW6000.BulkManager.csproj
dotnet run --project .\src\CW6000.BulkManager\CW6000.BulkManager.csproj
```

Before creating presets, open:

`Printing Preferences -> Media Definition`

Then load a CSV and run **Test 1 Preset** first.

## CSV columns

The manager accepts these primary columns:

```csv
Preset Name,Width (mm),Height (mm)
40x40,40,40
50x30,50,30
```

It automatically rotates a preset when the original width is above 112 mm but the other dimension fits the printer width.

## Safety

Always run the unique one-preset test before starting the full batch. Existing names can be rejected by the Epson dialog.
