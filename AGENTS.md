# OPTISENS Agent Instructions

## 1. Automatic Versioning & Changelog
After completing any feature, bugfix, or UI/behavior change—unless the user explicitly asks to skip versioning—automatically:

1. **Increment the patch version** by `+0.0.1` (e.g., `0.5.4` → `0.5.5`) in all of the following files:
   - `Directory.Build.props` (`<Version>`, `<AssemblyVersion>`, `<FileVersion>`)
   - `README.md` (version badge and Windows installer filename)
   - `docs/INSTALLATION.md` (Windows installer filename)
   - `src/OPTISENS.App/ViewModels/SetupViewModel.cs` (fallback version string)
   - `packaging/windows/OPTISENS.iss` (`#define MyAppVersion`)
   - `packaging/macos/Info.plist` (`CFBundleShortVersionString` and `CFBundleVersion`)
   - `packaging/macos/build-pkg.sh` (`VERSION=${1:-...}`)
   - `packaging/linux/build-deb.sh` (`VERSION=${1:-...}`)
   - `packaging/linux/build-appimage.sh` (`VERSION=${1:-...}`)
   *(Tip: run `git grep -n "<current-version>"` to verify no occurrences were missed.)*

2. **Update `CHANGELOG.md`:**
   - Prepend a new `## [X.Y.Z] - YYYY-MM-DD` section at the top of `CHANGELOG.md` categorizing changes under `Added`, `Fixed`, `Changed`, and `Removed`.

## 2. Build & Test Verification
- `Directory.Build.props` sets `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`; always verify **0 warnings and 0 errors**.
- Run `dotnet build OPTISENS.sln --no-incremental --verbosity quiet` and `dotnet test OPTISENS.sln --no-build --verbosity quiet` after final edits.
- If a running `OPTISENS.App` process locks `bin/Debug`, verify `OPTISENS.App` with `dotnet build src/OPTISENS.App/OPTISENS.App.csproj -c Release --verbosity quiet` and `dotnet test OPTISENS.sln -c Release --verbosity quiet`.
