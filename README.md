![Mal.DockingAid](Mal.DockingAid/thumb.png)

An in-cockpit LCD app for Space Engineers that turns any text panel into a
live docking indicator. Each enabled connector scans for the nearest valid
target on a *different* mechanical construct; the LCD picks whichever pair
is currently tracking and renders it from the pilot's frame. The chevrons
follow stick-direction semantics: push pitch / yaw / roll toward the chevron
and the error nulls — the rule holds on forward, aft, side, top and bottom
mounts, no mental remap per mount.

## What it shows

- **Reticle + cross** — your bore frame
- **Projected target ring** — where the partner connector is, foreshortened
  by relative tilt
- **Pitch / yaw notches on the cross** — input needed on the matching stick
- **Roll chevron on the rim** — input needed on the roll stick
- **Range / closure rate** — top-left / top-right numeric readouts
- **Connector name** — bottom-centre, so you know which pair is locked in

Status colours follow the standard alignment ladder: green when all three
(lateral / forwards / mating-roll) are inside the docked band; amber for the
warn band; red outside that.

## Setup

1. On your source connector, enable **Used for docking** (terminal checkbox
   — defaults to ON for newly placed connectors, sits just under the vanilla
   parking checkbox). Adjust the **Docking detection range** slider below it
   if needed (1–50 m, default 20 m).
2. The source ship needs a working, broadcasting **radio antenna**. The
   target ship needs one too, and their broadcast ranges have to overlap —
   the same "mutual antenna" rule SE uses for ID broadcasts.
3. On any LCD on the same mechanical construct as the source connector,
   pick the **Docking Aid** script.
4. Sit in any cockpit / control seat on that construct. The LCD orients
   itself to the active pilot's frame.

A target counts only if it's also a connector marked "Used for docking",
on a *different* mechanical construct, within the source's detection range,
and its bore is within 45° of anti-parallel to the source's bore (i.e.
roughly facing it).

## Building

MDK2-based. From the repo root:

```powershell
dotnet build Mal.DockingAid.slnx
```

Tests:

```powershell
dotnet test Mal.DockingAid.Tests/Mal.DockingAid.Tests.csproj -p:Platform=x64
```

The MDK2 packager emits the deployable mod folder on build; copy or symlink
its output into `%AppData%\SpaceEngineers\Mods\` to load locally.
