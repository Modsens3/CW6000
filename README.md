# CW6000 ColorWorks Bulk Manager

Windows tools for inspecting and automating Epson ColorWorks CW-C6000Ae media definitions.

## Initial scope

- UI Automation Inspector for the Epson driver
- Export of `AutomationId`, `Name`, `ControlType`, `ClassName`, bounds and supported patterns
- JSON snapshot of the Epson **Media Definition** and **New** dialogs
- Foundation for bulk media creation from CSV

## Technology

- C#
- .NET 8
- WPF
- Windows UI Automation

## Status

The first milestone is the UI Inspector. It will capture the real automation tree exposed by the Epson driver so the bulk creator can target stable controls instead of screen coordinates.
