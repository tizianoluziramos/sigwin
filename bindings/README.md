# bindings/

Generated C# P/Invoke bindings for the Rust core's C ABI.

- `NativeMethods.g.cs` is produced by [`csbindgen`](https://github.com/Cysharp/csbindgen) during
  the Rust build — see [`core/sigwin-core/build.rs`](../core/sigwin-core/build.rs). It is
  namespace `Sigwin.Interop`, class `NativeMethods`, and P/Invokes `sigwin_core.dll`.
- **Do not edit `*.g.cs` by hand** — regenerate by running `cargo build` in `core/`.
- `*.g.cs` is **gitignored, not committed** (see the repo `.gitignore`). It is generated at build
  time, so **build `core/` before `app/`**. The `app/` project will include it via a
  `<Compile Include>` once we wire the FFI (Phase 2).

It does not exist on a fresh clone; run `cargo build` in `core/` to generate it (requires the
toolchain in [../docs/setup.md](../docs/setup.md)).
