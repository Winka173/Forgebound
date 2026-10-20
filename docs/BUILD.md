# Build, test & tooling commands

Headless (works in this repo's CI container):
```
cd tools/dotnet/Forgebound.Tests && dotnet test                       # 78 NUnit tests (same files as Unity EditMode)
cd tools/dotnet/Forgebound.Cli && dotnet run -- validate              # content + proc graph + localisation
dotnet run -- loc-check | tooltips --lang vi | sim --seeds 16 | tournament --builds 600 | scenarios | play --fm blacksmith --seed 1 | duel ...
cd tools/dotnet/PresentationCheck && dotnet build                     # stub compile of Presentation (not a Unity verification)
python3 ArtSource/blender/build_all.py && python3 tools/art/atlas_packer.py && python3 tools/art/art_validator.py
python3 -m pytest tools/art/tests -q
python3 tools/audio/synth.py && python3 tools/audio/audio_validator.py
```
Unity (requires Unity 2022.3 LTS + platform modules; **not executed yet**, KI-01/KI-02):
```
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults reports/unity_editmode.xml
Unity -batchmode -quit -projectPath . -executeMethod Forgebound.EditorTools.BuildScript.BuildWindows
FORGEBOUND_KEYSTORE=... FORGEBOUND_KEYSTORE_PASS=... FORGEBOUND_KEY_ALIAS=... FORGEBOUND_KEY_PASS=... \
Unity -batchmode -quit -projectPath . -executeMethod Forgebound.EditorTools.BuildScript.BuildAndroid
Unity -batchmode -quit -projectPath . -executeMethod Forgebound.EditorTools.BuildScript.ExportIOS   # then open Builds/iOS in Xcode on macOS, set team & sign
```
