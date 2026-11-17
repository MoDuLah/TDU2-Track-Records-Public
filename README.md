# TDU2 Track Records Public

A Windows companion for Test Drive Unlimited 2. Log laps and event results, browse the
track catalogue, and see your best times in Power Laps. Dealerships and vehicle
specifications are read-only. The public edition omits Drag Stats, Drag Attempts,
Brake Attempts, and Vehicle Management.

The included SQLite database starts with the game catalogue and an empty lap history.
Your results are saved in the `database.db` next to the application.

## Build

1. Install Visual Studio with the **.NET desktop development** workload and the
   **.NET Framework 4.8 targeting pack**.
2. Open `TDU2 Track Records.sln` and build the **Release** configuration. Visual Studio
   restores the SQLite package through NuGet. From a Visual Studio Developer Prompt,
   you can also run `msbuild "TDU2 Track Records.sln" -restore -p:Configuration=Release`.
3. Run `TDU2 Track Records Public.exe` from `TDU2 Track Records/bin/Release`. Keep the
   other files in that folder with the executable and use a folder you can write to.

Lap and event logging works without a game save. Optional live profile sync requires an
existing TDU2 SaveGame Editor installation in your Documents folder with its Python
environment and the Track Records bridge (`tools/track_records_bridge.py`). With a
compatible save profile, sync can populate your garage and read-only objectives.
