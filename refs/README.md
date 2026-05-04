# Reference DLLs

This folder must contain three Emby DLLs for compilation:

- `MediaBrowser.Common.dll`
- `MediaBrowser.Controller.dll`
- `MediaBrowser.Model.dll`

**These files are NOT included in the repository** (excluded by `.gitignore`)
because Emby's binaries are not freely redistributable.

## How to obtain them

### Option A: From your local Emby Server install

| OS / Deployment | Path |
|---|---|
| Windows | `%AppData%\Emby-Server\system\` |
| Docker (`emby/embyserver`) | `docker cp <container>:/system/MediaBrowser.Common.dll ./refs/` (repeat for the other two) |
| Linux deb | `/opt/emby-server/system/` |

### Option B: From Emby's official release archive

```bash
EMBY_VERSION="4.9.0.34"   # change to the minimum version you want to support
curl -fL -o emby.tgz \
  "https://github.com/MediaBrowser/Emby.Releases/releases/download/${EMBY_VERSION}/embyserver-linux-x64-${EMBY_VERSION}.tgz"
tar -xzf emby.tgz
cp emby-server/system/MediaBrowser.Common.dll refs/
cp emby-server/system/MediaBrowser.Controller.dll refs/
cp emby-server/system/MediaBrowser.Model.dll refs/
```

> **Tip**: Use the **lowest Emby version** you want to support. Building against
> a newer version may cause `MissingMethodException` when users run an older Emby.
