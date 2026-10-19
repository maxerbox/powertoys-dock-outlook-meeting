# Outlook (on-premises) 2019 meetings in your PowerToys Command Palette dock

Countdown to your next Outlook meeting, directly in the PowerToys Command
Palette **Dock**, with the meeting name and room next to it.

Built for **on-premises Outlook 2019** (works with any desktop Outlook profile).

## The dock

![alt text](image.png)

## What you get

**Dock band** (Title + Icon only, like every dock widget):

| State                 | Dock shows                | Icon   |
| --------------------- | ------------------------- | ------ |
| Meeting in progress   | `Subject · 23m left`      | green  |
| Next meeting > 15 min | `Subject · 47m`           | gray   |
| Next meeting ≤ 15 min | `⚠ Subject · 12m` + toast | orange |
| Nothing left today    | `No more meetings`        | gray   |
| Outlook unreachable   | `Outlook ⚠`               | gray   |

Hovering a band button shows the full details

**Palette page** (`Outlook Next Meeting` in Command Palette): today's full
agenda with per-meeting countdown.

## Build & install

Requires the .NET 10 SDK and the `localwinsdk` shim. If you don't have the
Windows SDK 10.0.26100 installed, create the shim once with
`powershell -ExecutionPolicy Bypass -File scripts\create-winsdk-shim.ps1`
(it reuses the tooling from any installed Windows SDK).

```powershell


dotnet publish OutlookMeetingDock\OutlookMeetingDock.csproj -c Release -r win-x64 `
  -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true -p:AppxPackageDir=..\AppxPackages -p:AppxBundle=Never

# Sign with your locally-trusted dev cert (adjust the thumbprint)
& "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22000.0\x64\signtool.exe" sign /fd SHA256 `
  /sha1 <your-cert-thumbprint> AppxPackagesOutlookMeetingDock_1.0.0.0_x64_Test\OutlookMeetingDock_1.0.0.0_x64.msix

Add-AppxPackage AppxPackagesOutlookMeetingDock_1.0.0.0_x64_Test\OutlookMeetingDock_1.0.0.0_x64.msix
```

Then restart the Command Palette host (kill `Microsoft.CmdPal.UI`, it
auto-restarts, or relaunch via
`shell:AppsFolder\Microsoft.CommandPalette_8wekyb3d8bbwe!App`).
