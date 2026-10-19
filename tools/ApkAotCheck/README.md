# ApkAotCheck

Checks that every AOT image in a .NET for Android APK was compiled from the assemblies the APK ships (#368). That is what broke in #366: with `AndroidLinkMode=None` in Release, the SDK packaged the assemblies its `LinkAssembliesNoShrink` and `RewriteMarshalMethods` steps had modified, but gave the AOT compiler the unmodified copies, and the app aborted at startup (`class-init.c:2474 klass->instance_size == instance_size`). The Debug build has no AOT images, so it never showed.

```sh
dotnet run --project tools/ApkAotCheck -- artifacts/bin/Broadside.Viewer.Maui.Sample/release_net10.0-android/dev.broadside.viewer.sample-Signed.apk
```

Exit code 0 when every image matches, 1 on a mismatch (one line per problem) or when the APK has no AOT images at all (a Debug build here means the CI step changed), 2 for a bad command line or an APK it cannot read.

## How it works

Mono writes into each AOT image, as upper-case strings, the MVID of the assembly it compiled and of every assembly that one references; at startup it rejects an image whose MVIDs disagree with the loaded assemblies. For each ABI under `lib/` the tool:

1. Reads `libassembly-store.so`: the ELF section `payload` holds the store (`XABA`, version 3), one descriptor and name per assembly, and the data, LZ4-compressed behind an `XALZ` header in Release. It takes each assembly's MVID with `System.Reflection.Metadata`.
2. For every `libaot-<assembly>.so`, requires that the image holds `<assembly>`'s MVID ("compiled from a different X than the one packaged") and that every MVID it holds belongs to an assembly the APK ships ("compiled against an assembly ... which the APK does not ship").

A new assembly store layout fails the run with exit code 2 and says so, rather than passing silently.

## Where it runs

- `ci.yml`, job `maui`: on every pull request, after the Release build of `samples/Broadside.Viewer.Maui.Sample` (arm64 and x86_64).
- `maui-android.yml`: before the emulator smoke run of the same sample.
