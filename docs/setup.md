# Setup

## Preflight results (this machine, verified 2026-07-10)

| Tool | Needed for | Status |
|------|-----------|--------|
| rustup 1.28.2 | Rust toolchain manager | ✅ present |
| rustc / cargo 1.93.0 (`x86_64-pc-windows-msvc`) | Rust core (MSVC host — required, **not** GNU) | ✅ present (≥ MSRV 1.89) |
| git 2.55 | version control | ✅ present |
| .NET SDK 10.0.301 | C# app build | ✅ present |
| Visual Studio 2026 Community + WinUI/.NET Desktop workload | WinUI 3 app | ✅ present (app builds & runs) |
| **MSVC C++ toolset** ("Desktop development with C++") | Rust MSVC linker + BoringSSL build | ❌ **missing — install** |
| **libclang** (Clang tools / LLVM) | `bindgen` in libsignal | ❌ **missing — install** |
| **protoc** | protobuf codegen in libsignal/presage | ❌ **missing — install** |
| **CMake** | BoringSSL build | ❌ **missing — install** |
| **NASM** | BoringSSL assembly on Windows | ❌ **missing — install** |
| vcpkg / system OpenSSL | only if sqlcipher needs system OpenSSL | ⚪ optional (plan: vendored OpenSSL, see [architecture](architecture.md#the-boringssl--openssl-question)) |

The **C# app builds today**. Only the **Rust core** is blocked, and only until the five missing
items above are installed. Nothing in the Rust core can even *link* until the MSVC C++ toolset is
present.

## Install the missing prerequisites

### 1. MSVC C++ toolset + Clang (via the Visual Studio Installer)

Open the **Visual Studio Installer**, click **Modify** on Visual Studio 2026, and under
**Workloads** add:

- **Desktop development with C++** — provides `cl.exe`, `link.exe`, and the Windows SDK that the
  Rust MSVC target links against, plus the C toolchain BoringSSL needs.
- In that workload's **Installation details** pane, also tick **C++ Clang tools for Windows**
  (this provides `libclang`, required by `bindgen`). *Alternatively* install standalone LLVM
  (below) and point `LIBCLANG_PATH` at it.

### 2. Command-line tools (via winget)

```powershell
winget install Google.Protobuf -e   # protoc
winget install Kitware.CMake   -e   # cmake
winget install NASM.NASM       -e   # nasm
winget install LLVM.LLVM       -e   # libclang (skip if you installed VS "C++ Clang tools")
```

After installing, **open a new terminal** and confirm they are on `PATH`:

```powershell
protoc --version; cmake --version; nasm -v
```

If `bindgen` later complains it cannot find `libclang`, set:

```powershell
setx LIBCLANG_PATH "C:\Program Files\LLVM\bin"
```

(or the `...\VC\Tools\Llvm\x64\bin` folder inside your VS install if you used the VS Clang
component).

### 3. OpenSSL for sqlcipher (via vcpkg)

`presage-store-sqlite` uses sqlcipher, whose bundled SQLite links `libcrypto`, so the build needs
OpenSSL. We use **vcpkg** (the standard Windows C/C++ package manager) to provide a **dynamically
linked OpenSSL 3.x**. Dynamic linking is deliberate: libsignal statically links BoringSSL, and
keeping OpenSSL in its own DLL avoids duplicate-symbol collisions between the two crypto libraries
at link time.

```powershell
cd C:\Users\yusif
git clone https://github.com/microsoft/vcpkg
.\vcpkg\bootstrap-vcpkg.bat
.\vcpkg\vcpkg install openssl:x64-windows
setx OPENSSL_DIR "C:\Users\yusif\vcpkg\installed\x64-windows"   # reopen terminal after
```

`libsqlite3-sys` finds OpenSSL through `OPENSSL_DIR`. At runtime the app needs
`libcrypto-3-x64.dll` (in `…\vcpkg\installed\x64-windows\bin`) — add that folder to `PATH`, or copy
the DLL next to the executable.

> Do **not** switch to a statically vendored OpenSSL (`bundled-sqlcipher-vendored-openssl`): static
> OpenSSL + static BoringSSL in one binary is the coexistence/link-collision risk noted in
> [architecture.md](architecture.md#the-boringssl--openssl-question).

## Building

```powershell
# Rust core (headless) — Phase 0 smoke test, then Phase 1 CLI harness
cd core
cargo build
cargo run -p harness        # prints the core ABI version (Phase 0)

# C# app
# Open signal-windows-client.slnx in Visual Studio 2026 and press F5,
# or:  dotnet build app/signal-windows-client.csproj
```

The Rust build regenerates the C# bindings into `../bindings/` via `csbindgen` (see
[`core/sigwin-core/build.rs`](../core/sigwin-core/build.rs)).
