# Running Sigwin

Three separate ways to run this project, from simplest/headless to full UI. All three assume
the toolchain in [setup.md](setup.md) is installed and `cd core && cargo build` has succeeded at
least once (it also regenerates the C# bindings in `bindings/`).

## 1. Rust core alone (`harness`)

A headless CLI that proves the core works end to end with no UI: it links to your phone (QR code
printed to the terminal), then streams incoming messages to stdout.

```powershell
cd core
cargo run -p harness
```

Config via environment variables (all optional):

| Variable            | Purpose                                        | Default                |
| ------------------- | ---------------------------------------------- | ---------------------- |
| `SIGWIN_DB`         | path to the sqlite store                       | `sigwin.db`            |
| `SIGWIN_PASSPHRASE` | sqlcipher passphrase                           | unencrypted (dev only) |
| `SIGWIN_DEVICE`     | linked-device display name shown on your phone | `Sigwin`               |
| `RUST_LOG`          | tracing filter                                 | `info`                 |

Scan the printed QR with **Signal → Linked devices → Link new device**. Once linked, the same
`SIGWIN_DB` store can be reused by `console-test` or the app without re-linking.

## 2. FFI smoke test (`console-test`)

A small C# console app that calls the same native `sigwin_core.dll` the WinUI app uses, through
the generated P/Invoke bindings — useful for confirming the C ABI and callback bridge work, or for
capturing raw JSON events while debugging, without launching the full UI.

```powershell
dotnet run --project console-test -- <db-path> [passphrase]

# Reuse a store the harness already linked, to skip the QR:
dotnet run --project console-test -- C:\path\to\core\sigwin.db devpass
```

Prints every incoming event as JSON. Press Enter to stop.

## 3. The app

The actual WinUI 3 desktop client.

```powershell
# From Visual Studio: open signal-windows-client.slnx and press F5, or:
dotnet build app/signal-windows-client.csproj -c Debug -p:Platform=x64
```

The app stores its own encrypted database under
`%LOCALAPPDATA%\Packages\<package-id>\LocalState\sigwin.db` (separate from whatever store you
used for `harness`/`console-test` — link it independently on first launch).
