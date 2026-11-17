# TDU2 Track Records Public

A Windows companion for Test Drive Unlimited 2. Log laps and event results, browse
tracks and dealerships, see your best times in Power Laps, and sync garage mileage
and objective progress from your game save. The public edition omits Drag Stats,
Drag Attempts, Brake Attempts, and the Vehicle Management editor.

## Run the Windows release

Extract the complete Windows ZIP to a writable folder and run
`TDU2 Track Records Public.exe`. Keep the DLLs, the `Decoder` folder, and
`database.seed.db` with the executable. The app needs .NET Framework 4.8.
The bundled decoder reads TDU2 saves without modifying them.

The included catalogue starts with no lap records and zero mileage on all 208 cars.
On first launch the app copies the seed to
`%LocalAppData%\MoDuLah\TDU2 Track Records\database.db`. Subsequent releases leave
that database in place. If you already logged laps in public v2.4.0, back up its
`database.db` from beside the old executable. Before your first 2.5 launch, copy that
file to the new location if there is no database there. Keep both copies if you
already have a database at the new location.

## Build from source

Install Visual Studio with the **.NET desktop development** workload and the
**.NET Framework 4.8 targeting pack**, plus Python with PyInstaller
(`python -m pip install pyinstaller`). From PowerShell, run
`./build_public_release.ps1` to build the decoder and the application and create the
portable ZIP in `artifacts`. See [third-party notices](THIRD_PARTY_NOTICES.md) for
the decoder licence.
